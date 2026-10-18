using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MpvNet.Windows.Native;

namespace MpvNet.Windows.Modern;

public sealed record ForwardedOpenRequest(int Protocol, string Identity, string Mode, string[] Files,
    Dictionary<string, string> Options, string? Command)
{
    public static ForwardedOpenRequest Parse(string identity, string mode, string[] arguments)
    {
        var files = new List<string>();
        var options = new Dictionary<string, string> { ["start"] = "none", ["resume-playback"] = "yes" };
        string? command = null;
        foreach (string arg in arguments) {
            if (arg == "--queue") { mode = "queue"; continue; }
            if (arg.StartsWith("--command=")) { command = arg[10..]; mode = "command"; continue; }
            if (arg.StartsWith("--")) {
                int equal = arg.IndexOf('=');
                string name = equal >= 0 ? arg[2..equal] : arg[2..];
                string value = equal >= 0 ? arg[(equal + 1)..] : "yes";
                if (equal < 0 && name.StartsWith("no-")) { name = name[3..]; value = "no"; }
                // Startup/process options belong to the receiving instance.
                // File options, including explicit start=0, stay with this load.
                if (name is "process-instance" or "config" or "config-dir" or "idle" or "input-ipc-server" or
                    "terminal" or "input-terminal" or "input-conf" or "load-scripts" or "scripts" or
                    "log-file" or "really-quiet" or "vo" or "ao" or "force-window") continue;
                options[name] = value;
            } else if (arg == "-" || arg.Contains("://")) files.Add(arg);
            else if (File.Exists(arg)) files.Add(Path.GetFullPath(arg));
        }
        return new(1, identity, mode, files.ToArray(), options, command);
    }
}

public static class InstanceRouting
{
    sealed record Registration(string Identity, int Pid, long Started, string Executable, long Window);
    static string _identity = "";
    static string _directory = "";
    static string? _registration;
    public static string Identity => _identity;

    public static string Initialize(string executable, string configuration)
    {
        string value = Path.GetFullPath(executable).ToUpperInvariant() + "\n" + Path.GetFullPath(configuration).ToUpperInvariant();
        _identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        _directory = Path.Combine(Path.GetDirectoryName(configuration)!, ".modern-instances");
        return "FrameFlow-instance-" + _identity;
    }

    public static void Register(IntPtr window)
    {
        Directory.CreateDirectory(_directory);
        using var current = Process.GetCurrentProcess();
        var record = new Registration(_identity, current.Id, current.StartTime.ToUniversalTime().Ticks,
            Environment.ProcessPath!, window.ToInt64());
        _registration = Path.Combine(_directory, _identity + "-" + current.Id + ".json");
        File.WriteAllText(_registration, JsonSerializer.Serialize(record));
    }

    public static void Unregister()
    {
        try { if (_registration != null) File.Delete(_registration); }
        catch (IOException) { }
    }

    public static bool Forward(ForwardedOpenRequest request)
    {
        if (!Directory.Exists(_directory)) return false;
        var registrations = new List<Registration>();
        foreach (string path in Directory.EnumerateFiles(_directory, _identity + "-*.json")) {
            try {
                var record = JsonSerializer.Deserialize<Registration>(File.ReadAllText(path));
                if (record != null && record.Identity == _identity && record.Pid != Environment.ProcessId) registrations.Add(record);
            } catch (IOException) { }
            catch (JsonException) { }
        }
        foreach (var record in registrations.OrderBy(x => x.Started)) {
            try {
                using var process = Process.GetProcessById(record.Pid);
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != record.Started ||
                    !string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) continue;
                var window = new IntPtr(record.Window);
                GetWindowThreadProcessId(window, out uint windowPid);
                if (windowPid != record.Pid) continue;
                WinApi.AllowSetForegroundWindow(record.Pid);
                string payload = JsonSerializer.Serialize(request);
                var data = new WinApi.CopyDataStruct { lpData = payload, cbData = (payload.Length + 1) * 2 };
                if (SendMessageTimeout(window, 0x004A, IntPtr.Zero, ref data, 2, 2000, out var result) != IntPtr.Zero && result != IntPtr.Zero)
                    return true;
            } catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return false;
    }

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr parameter,
        ref WinApi.CopyDataStruct data, uint flags, uint timeout, out IntPtr result);
}
