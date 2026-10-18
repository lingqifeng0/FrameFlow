// Profile this process's own player and settings controls, never another app.
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using MpvNet;
using MpvNet.MVVM;
using MpvNet.Windows.Modern;
using MpvNet.Windows.UI;
using MpvNet.Windows.WinForms;
using MpvNet.Windows.WPF;
using static MpvNet.Global;

internal static class Program
{
    [STAThread] static int Main()
    {
        string root=Environment.GetEnvironmentVariable("MPVNET_TEST_ROOT")!;
        string media=Environment.GetEnvironmentVariable("MPVNET_TEST_MEDIA")!;
        string label=Environment.GetEnvironmentVariable("MPVNET_MEMORY_LABEL")??"baseline";
        string output=Path.Combine(root,"test-results/memory-v23",label);
        string config=Path.Combine(output,"config-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(config);
        Environment.SetEnvironmentVariable("MPVNET_HOME",config);
        File.WriteAllText(Path.Combine(config,"mpvnet.conf"),"process-instance=multi\nauto-load-folder=no\nstart-size=always\n");
        File.WriteAllText(Path.Combine(config,"mpv.conf"),"osc=no\nborder=no\ntitle-bar=no\nkeepaspect-window=no\nkeep-open=yes\nvo=gpu\nhwdec=auto-safe\nao=null\n");
        File.WriteAllText(Path.Combine(config,"modern-ui.json"),JsonSerializer.Serialize(new {Width=2400,Height=1400,WindowDpi=168,AutoFolderPlaylist=false,PlaylistVisible=false}));
        var library=NativeLibrary.Load(Path.Combine(root,"dist/FrameFlow/libmpv-2.dll"));
        var info=NativeLibrary.Load(Path.Combine(root,"dist/FrameFlow/MediaInfo.dll"));
        NativeLibrary.SetDllImportResolver(typeof(MpvNet.Native.LibMpv).Assembly,(name,_,_)=>name switch {"libmpv-2.dll"=>library,"MediaInfo.dll"=>info,_=>IntPtr.Zero});
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Translator.Current=new WpfTranslator(); App.Init(); Theme.Init(); InstanceRouting.Initialize(Environment.ProcessPath!,App.ConfPath);
        using var host=new MainForm();
        var samples=new List<object>(); var weakWindows=new List<WeakReference>();
        var clock=Stopwatch.StartNew(); Exception? failure=null;
        object? Call(string name,params object[] values)=>typeof(MainForm).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(host,values);
        T Field<T>(string name)=>(T)typeof(MainForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;
        void Sample(string stage,ConfWindow? window=null) {
            using var process=Process.GetCurrentProcess(); process.Refresh(); var gc=GC.GetGCMemoryInfo();
            var controls=window==null?0:((System.Collections.IDictionary)typeof(ConfWindow).GetField("_settingControls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Count;
            var sample=new { stage, milliseconds=clock.ElapsedMilliseconds, workingMb=process.WorkingSet64/1048576d,
                privateMb=process.PrivateMemorySize64/1048576d, managedMb=GC.GetTotalMemory(false)/1048576d,
                heapMb=gc.HeapSizeBytes/1048576d, fragmentedMb=gc.FragmentedBytes/1048576d,
                allocatedMb=GC.GetTotalAllocatedBytes(false)/1048576d, gen2=GC.CollectionCount(2), handles=process.HandleCount,
                controls, closedWindowsStillAlive=weakWindows.Count(reference=>reference.IsAlive),
                hwdec=Player.GetPropertyString("hwdec-current"), cache=Player.GetPropertyString("demuxer-cache-state") };
            samples.Add(sample); Console.WriteLine(JsonSerializer.Serialize(sample));
        }
        async Task Until(Func<bool> test,string reason,int timeout=10000) { var timer=Stopwatch.StartNew(); while(!test()) {if(timer.ElapsedMilliseconds>timeout) throw new Exception(reason); await Task.Delay(25);} }
        async Task ExerciseSettings(int round) {
            var window=new ConfWindow("快捷键与鼠标:"){ShowActivated=false}; window.Show();
            await Task.Delay(150); Sample("settings-open-"+round,window);
            // Visit every native category once, then revisit without creating
            // new settings models. This matches heavy use of the settings UI.
            foreach(string category in window.FilterStrings.ToArray()) {window.NavigateTo(category+":"); await Task.Delay(12);}
            Sample("all-categories-"+round,window);
            for(int i=0;i<18;i++) {
                window.SearchText=i%3==0?"音量":i%3==1?"倍率":"缩放";
                Field<ModernSettings>("_modernSettings").TemporaryFastForwardSpeed=3+i%4;
                Call("SaveModernPreferences"); await Task.Delay(12);
            }
            Sample("settings-edited-"+round,window);
            weakWindows.Add(new WeakReference(window)); window.Close();
        }
        host.Shown+=async(_,_)=>{
            try {
                Player.CommandV("loadfile",media); await Until(()=>!Player.VideoSize.IsEmpty,"video failed to load");
                Player.SetPropertyBool("pause",false); await Task.Delay(800); Sample("playing-before-settings");
                for(int round=0;round<4;round++) { await ExerciseSettings(round); await Task.Delay(300); Sample("settings-closed-"+round); }
                await Task.Delay(1500); Sample("closed-idle-before-diagnostic-gc");
                // Only the test forces collection, to distinguish unreachable
                // garbage from strongly retained objects. Product code must
                // not introduce periodic forced collections or working-set trim.
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(250);
                Sample("closed-after-diagnostic-gc");
                var area=Screen.FromControl(host).WorkingArea;
                for(int round=0;round<24;round++) {
                    Call("QueueModernWindowResize",new Rectangle(area.X+100,area.Y+100,2200-round%3*250,1300-round%3*120));
                    Call("FlushModernResizeFrame"); await Task.Delay(16); Call("EndModernWindowResize");
                    await Until(()=>Field<ResizePreviewCanvas>("_resizePreview").Visible==false,"resize preview failed to finish");
                    if(round%6==5) Sample("resize-cycle-"+round);
                }
                await Task.Delay(600); Sample("resize-idle-before-diagnostic-gc");
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(200); Sample("resize-after-diagnostic-gc");
            } catch(Exception e) {failure=e;Console.Error.WriteLine(e);} finally {host.Close();}
        };
        Application.Run(host);
        File.WriteAllText(Path.Combine(output,"observations.json"),JsonSerializer.Serialize(new{label,samples,failure=failure?.ToString()},new JsonSerializerOptions{WriteIndented=true}));
        return failure==null?0:1;
    }
}
