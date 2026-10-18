// Exercise the actual MainForm and GPU player in this test's own process.
// No mouse/keyboard injection, cross-process window access, or user configuration.
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using MpvNet;
using MpvNet.Help;
using MpvNet.MVVM;
using MpvNet.Windows.Modern;
using MpvNet.Windows.UI;
using MpvNet.Windows.WinForms;
using MpvNet.Windows.WPF;
using static MpvNet.Global;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        string root = Environment.GetEnvironmentVariable("MPVNET_TEST_ROOT") ?? throw new Exception("Test root required");
        string output = Path.Combine(root,"test-results/resize-v27/live");
        string config = Path.Combine(output,"config");
        Directory.CreateDirectory(config);
        Environment.SetEnvironmentVariable("MPVNET_HOME",config);
        File.WriteAllText(Path.Combine(config,"mpvnet.conf"),"process-instance=multi\nauto-load-folder=no\nstart-size=always\n");
        File.WriteAllText(Path.Combine(config,"mpv.conf"),$"osc=no\nborder=no\ntitle-bar=no\nkeepaspect-window=no\nkeep-open=yes\nvo=gpu\nhwdec=no\nao=null\ninput-ipc-server=\\\\.\\pipe\\mpvnet-resize-integration-{Environment.ProcessId}\n");
        File.WriteAllText(Path.Combine(config,"modern-ui.json"),JsonSerializer.Serialize(new {
            Width=1450, Height=880, WindowDpi=168, AutoFolderPlaylist=false, PlaylistVisible=false,
            NightMode=true, NightStrength=35, VideoBrightness=-10,
            Toolbar=new[] { "frame-back","frame-forward","screenshot","bookmark","ab-loop","timestamp" }
                .Select(id => ToolbarCatalog.Find(id)!.Item)
        }));
        IntPtr library = NativeLibrary.Load(Path.Combine(root,"dist/FrameFlow/libmpv-2.dll"));
        IntPtr mediaInfo = NativeLibrary.Load(Path.Combine(root,"dist/FrameFlow/MediaInfo.dll"));
        NativeLibrary.SetDllImportResolver(typeof(MpvNet.Native.LibMpv).Assembly,(name,_,_) => name switch {
            "libmpv-2.dll" => library, "MediaInfo.dll" => mediaInfo, _ => IntPtr.Zero
        });
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Translator.Current = new WpfTranslator();
        App.Init();
        Theme.Init();
        InstanceRouting.Initialize(Environment.ProcessPath!, App.ConfPath);
        using var host = new MainForm();
        object? Call(string name, params object[] values) => typeof(MainForm).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(host,values);
        T Field<T>(string name) => (T)typeof(MainForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;
        JsonElement State() {
            var value=JsonSerializer.Deserialize<JsonElement>(Player.GetPropertyString("user-data/modern/window"));
            return value.ValueKind==JsonValueKind.String ? JsonSerializer.Deserialize<JsonElement>(value.GetString()!) : value;
        }
        var records = new List<JsonElement>();
        using var timer = new System.Windows.Forms.Timer { Interval=32 };
        int frames=0, groups=0;
        long firstLayout=0;
        bool running=false, labels=false, waitingFirst=false, waitingFinal=false;
        Exception? failure=null;
        double started=0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Fail(Exception exception) { failure=exception; timer.Stop(); host.Close(); }
        Application.ThreadException += (_,e) => Fail(e.Exception);
        timer.Tick += (_,_) => {
            try {
                if (clock.Elapsed.TotalSeconds>45) throw new Exception("Live resize timed out");
                if (Player.VideoSize.IsEmpty) return;
                if (waitingFirst || waitingFinal) {
                    var reveal=State().GetProperty("controls");
                    if (reveal.GetProperty("previewVisible").GetBoolean()) return;
                    if (reveal.GetProperty("previewRevealTimeouts").GetInt32()!=0 ||
                        Player.GetPropertyInt("osd-dimensions/w")!=Field<VideoSurface>("_videoSurface").ClientSize.Width ||
                        Player.GetPropertyInt("osd-dimensions/h")!=Field<VideoSurface>("_videoSurface").ClientSize.Height)
                        throw new Exception("Release revealed the GPU before it rendered its final size");
                    records.Add(State().Clone());
                    if (waitingFinal) {
                        Call("RememberModernWindowSize"); Call("SaveModernSettings");
                        Console.WriteLine($"PASS: {frames} preview resize frames, GPU frozen during two gestures, full backgrounds, verified final render and six distinct tools");
                        timer.Stop(); host.Close(); return;
                    }
                    waitingFirst=false;
                    Field<ModernSettings>("_modernSettings").ShowButtonLabels=true;
                    Call("RebuildToolbar"); Call("PublishModernWindowState");
                    firstLayout=State().GetProperty("toolbar").GetProperty("layoutPasses").GetInt32();
                    groups++; labels=true;
                }
                if (!running) {
                    Player.SetPropertyBool("pause",false);
                    started=Player.GetPropertyDouble("time-pos");
                    firstLayout=State().GetProperty("toolbar").GetProperty("layoutPasses").GetInt32();
                    running=true;
                }
                if (groups==0 && frames==60) {
                    Call("EndModernWindowResize");
                    var restored=State();
                    if (restored.GetProperty("toolbar").GetProperty("layoutPasses").GetInt64()!=firstLayout+1)
                        throw new Exception("First release rearranged the toolbar more than once");
                    waitingFirst=true;
                    return;
                }
                var area=Screen.FromControl(host).WorkingArea;
                int width=980+(frames*37)%620, height=610+(frames*29)%330;
                var target=new Rectangle(area.X+300+frames%71,area.Y+150+frames%53,width,height);
                // Deliver a burst as one drag would. Only the last pending size
                // may remain after a frame, including the final mouse-up flush.
                Call("QueueModernWindowResize",new Rectangle(target.X,target.Y,width+2,height+2));
                Call("QueueModernWindowResize",new Rectangle(target.X,target.Y,width+1,height+1));
                Call("QueueModernWindowResize",target);
                Call("FlushModernResizeFrame");
                if (host.Bounds!=target) throw new Exception("Resize lost the latest pointer position");
                var state=State(); var chrome=state.GetProperty("controls"); var toolbar=state.GetProperty("toolbar");
                if (!state.GetProperty("resizing").GetBoolean() || chrome.GetProperty("headerColor").GetString()!="#000000" ||
                    chrome.GetProperty("headerContentVisible").GetBoolean() || chrome.GetProperty("footerContentVisible").GetBoolean())
                    throw new Exception("A live frame exposed toolbar content/blue paint");
                if (host.Region == null || host.Region.IsVisible(0,0) || host.Region.IsVisible(host.Width-1,0) ||
                    host.Region.IsVisible(0,host.Height-1) || host.Region.IsVisible(host.Width-1,host.Height-1))
                    throw new Exception("A live frame lost the rounded outer contour");
                if (chrome.GetProperty("resizeNativeLayoutFailures").GetInt32()!=0 ||
                    chrome.GetProperty("resizeRepaints").GetInt32()<chrome.GetProperty("resizeFrames").GetInt32())
                    throw new Exception("A live frame lost native coordinates or a complete repaint");
                if (toolbar.GetProperty("total").GetInt32()!=6 || toolbar.GetProperty("layoutPasses").GetInt64()!=firstLayout)
                    throw new Exception("A live frame rearranged/dropped custom buttons");
                var cover=Field<ResizePreviewCanvas>("_resizePreview");
                if (!cover.Visible || !cover.HasFrame || Field<Panel>("_modernBody").Visible ||
                    chrome.GetProperty("videoWidth").GetInt32()!=chrome.GetProperty("frozenVideoWidth").GetInt32() ||
                    chrome.GetProperty("videoHeight").GetInt32()!=chrome.GetProperty("frozenVideoHeight").GetInt32() ||
                    chrome.GetProperty("previewFailures").GetInt32()!=0)
                    throw new Exception("Resize exposed/resized the GPU instead of a complete preview: "+chrome.GetRawText());
                using (var image=new Bitmap(cover.Width,cover.Height)) {
                    cover.DrawToBitmap(image,cover.ClientRectangle);
                    foreach (var point in new[]{new Point(0,0),new Point(image.Width-1,0),new Point(0,image.Height-1),new Point(image.Width-1,image.Height-1)})
                        if (image.GetPixel(point.X,point.Y).ToArgb()!=Color.Black.ToArgb()) throw new Exception("Preview background has an unpainted edge");
                    if (frames%30==0) {
                        int warmSamples=0;
                        for(int y=image.Height/4;y<image.Height*3/4;y+=16) for(int x=image.Width/4;x<image.Width*3/4;x+=16) {
                            var pixel=image.GetPixel(x,y);
                            if(pixel.R>150 && pixel.R>pixel.G+6 && pixel.G>pixel.B+12) warmSamples++;
                        }
                        if(warmSamples<30) throw new Exception("Resize preview lost the video brightness/night profile");
                        image.Save(Path.Combine(output,$"frame-{frames}.png"));
                    }
                }
                if (Player.GetPropertyBool("pause") || Player.GetPropertyString("current-vo")!="gpu")
                    throw new Exception("Resize interrupted GPU playback");
                records.Add(state.Clone());
                if (++frames<120) return;
                // Leave the final queued bounds for release to consume itself.
                var final=new Rectangle(target.X,target.Y,1100,720);
                Call("QueueModernWindowResize",final);
                Call("EndModernWindowResize");
                if (host.Bounds!=final) throw new Exception("Release lost its last queued bounds");
                var end=State(); records.Add(end.Clone());
                if (end.GetProperty("resizing").GetBoolean() || !end.GetProperty("rounded").GetBoolean() ||
                    end.GetProperty("controls").GetProperty("headerColor").GetString()!="#0096FF" ||
                    end.GetProperty("toolbar").GetProperty("layoutPasses").GetInt64()!=firstLayout+1 ||
                    end.GetProperty("toolbar").GetProperty("total").GetInt32()!=6 ||
                    !end.GetProperty("toolbar").GetProperty("overflow").GetBoolean() || !labels)
                    throw new Exception("Release did not restore rounded blue chrome and all six buttons exactly once");
                if (Player.GetPropertyDouble("time-pos")<=started+1) throw new Exception("Video did not advance through resizing");
                waitingFinal=true;
            } catch (Exception e) { Fail(e); }
        };
        host.Shown += (_,_) => timer.Start();
        Application.Run(host);
        File.WriteAllText(Path.Combine(output,"observations.json"),JsonSerializer.Serialize(new { frames,groups=groups+1,states=records,failure=failure?.ToString() },new JsonSerializerOptions { WriteIndented=true }));
        if (failure!=null) { Console.Error.WriteLine(failure); return 1; }
        return frames==120?0:1;
    }
}
