
using System.Windows.Forms;
using System.Threading;

using MpvNet.Windows.Native;
using MpvNet.Help;
using MpvNet.Windows.UI;
using MpvNet.Windows.Help;
using MpvNet.Windows.WPF;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows;

static class Program
{
    [STAThread]
    static void Main()
    {
        string[] workerArgs = Environment.GetCommandLineArgs();
        if (workerArgs.Length >= 3 && workerArgs[1] == "--restart-player" && int.TryParse(workerArgs[2], out int parentPid))
        {
            try { if (!Process.GetProcessById(parentPid).WaitForExit(15000)) return; }
            catch (ArgumentException) { }
            var restart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true };
            if (workerArgs.Length > 3) restart.ArgumentList.Add(workerArgs[3]);
            Process.Start(restart);
            return;
        }
        try
        {
            RegistryHelp.ProductName = AppInfo.Product;
            Translator.Current = new WpfTranslator();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppDomain.CurrentDomain.UnhandledException += (sender, e) => Terminal.WriteError(e.ExceptionObject);
            Application.ThreadException += (sender, e) => Terminal.WriteError(e.Exception);

            if (App.IsTerminalAttached)
                WinApi.AttachConsole(-1 /*ATTACH_PARENT_PROCESS*/);

            string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();

            if (args.Length > 0 && args[0] == "--register-file-associations")
            {
                FileAssociation.Register(args[1], args.Skip(1).ToArray());
                return;
            }

            App.Init();
            Theme.Init();
            using Mutex mutex = new Mutex(true, InstanceRouting.Initialize(Environment.ProcessPath!, App.ConfPath), out bool isFirst);

            if (Control.ModifierKeys == Keys.Shift ||
                App.CommandLine.Contains("--process-instance=multi") ||
                App.CommandLine.Contains("--o="))
            {
                App.ProcessInstance = "multi";
            }

            if ((App.ProcessInstance == "single" || App.ProcessInstance == "queue") && !isFirst)
            {
                var request = ForwardedOpenRequest.Parse(InstanceRouting.Identity, App.ProcessInstance, args);
                for (int i = 0; i < 30; i++)
                {
                    if (InstanceRouting.Forward(request)) {
                        if (App.IsTerminalAttached) WinApi.FreeConsole();
                        return;
                    }
                    Thread.Sleep(50);
                }
                // No matching responsive instance: open here, never send to an
                // unrelated mpvnet.exe or silently discard the user's request.
            }

            if (ProcessCommandLineArguments())
                Environment.GetCommandLineArgs();
            else if (App.CommandLine.Contains("--o="))
            {
                App.AutoLoadFolder = false;
                Player.Init(IntPtr.Zero, true);
                CommandLine.ProcessCommandLineArgsPostInit();
                CommandLine.ProcessCommandLineFiles();
                Player.SetPropertyString("idle", "no");
                Player.EventLoop();
                Player.Destroy();
            }
            else
                Application.Run(new WinForms.MainForm());

            if (App.IsTerminalAttached)
                WinApi.FreeConsole();

            InstanceRouting.Unregister();
        }
        catch (Exception ex)
        {
            Terminal.WriteError(ex);
        }
    }

    static bool ProcessCommandLineArguments()
    {
        foreach (string arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (arg == "--profile=help")
            {
                Player.Init(IntPtr.Zero, false);
                Console.WriteLine(Player.GetProfiles());
                Player.Destroy();
                return true;
            }
            else if (arg == "--vd=help" || arg == "--ad=help")
            {
                Player.Init(IntPtr.Zero, false);
                Console.WriteLine(Player.GetDecoders());
                Player.Destroy();
                return true;
            }
            else if (arg == "--audio-device=help")
            {
                Player.Init(IntPtr.Zero, false);
                Console.WriteLine(Player.GetPropertyOsdString("audio-device-list"));
                Player.Destroy();
                return true;
            }
            else if (arg == "--input-keylist")
            {
                Player.Init(IntPtr.Zero, false);
                Console.WriteLine(Player.GetPropertyString("input-key-list").Replace(",", BR));
                Player.Destroy();
                return true;
            }
            else if (arg == "--version")
            {
                Player.Init(IntPtr.Zero, false);
                Console.WriteLine(AppClass.About);
                Player.Destroy();
                return true;
            }
        }

        return false;
    }
}
