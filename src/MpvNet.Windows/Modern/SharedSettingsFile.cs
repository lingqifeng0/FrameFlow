using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace MpvNet.Windows.Modern;

// All processes sharing a portable configuration must update the same document
// under the same lock. Never write a cached copy over another window's changes.
public static class SharedSettingsFile
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Access<T>(string path, bool write, Func<JsonObject, T> action)
    {
        path = Path.GetFullPath(path);
        string identity = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        using var gate = new Mutex(false, "mpvnet-modern-settings-" + hash);
        bool entered = false;
        try {
            try { entered = gate.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { entered = true; }
            if (!entered) throw new IOException("播放器设置正在被另一窗口保存，请稍后重试。");
            JsonObject document = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("设置文件不是 JSON 对象。")
                : new();
            T result = action(document);
            if (write) {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporary = path + "." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try {
                    File.WriteAllText(temporary, document.ToJsonString(Options));
                    File.Move(temporary, path, true);
                } finally {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            return result;
        } finally {
            if (entered) gate.ReleaseMutex();
        }
    }
}
