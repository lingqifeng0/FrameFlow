// Exercise this process's own UI controls and mpv's public input API.
// No OS input injection or access to another app's windows/settings.
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using MpvNet;
using MpvNet.Windows;
using MpvNet.MVVM;
using MpvNet.Windows.Modern;
using MpvNet.Windows.UI;
using MpvNet.Windows.WinForms;
using MpvNet.Windows.WPF;
using MpvNet.Windows.WPF.Controls;
using static MpvNet.Global;

internal static class Program
{
    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [STAThread]
    static int Main()
    {
        string root = Environment.GetEnvironmentVariable("MPVNET_TEST_ROOT")!;
        string media = Environment.GetEnvironmentVariable("MPVNET_TEST_MEDIA")!;
        bool previewOnly=Environment.GetEnvironmentVariable("MPVNET_TEST_PREVIEW_ONLY")=="1";
        string output = Path.Combine(root,previewOnly?"test-results/controls-v27/preview":"test-results/controls-v27/live");
        string config = Path.Combine(output,"config-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(config);
        Environment.SetEnvironmentVariable("MPVNET_HOME",config);
        File.WriteAllText(Path.Combine(config,"mpvnet.conf"),"process-instance=multi\nauto-load-folder=no\nstart-size=always\n");
        File.WriteAllText(Path.Combine(config,"mpv.conf"),"osc=no\nborder=no\ntitle-bar=no\nkeepaspect-window=no\nkeep-open=yes\nautocreate-playlist=same\nvo=gpu\nhwdec=no\nao=null\n");
        File.WriteAllText(Path.Combine(config,"modern-ui.json"),JsonSerializer.Serialize(new {
            Width=1100, Height=720, WindowDpi=168, AutoFolderPlaylist=false, PlaylistVisible=false, Speed=1.25
        }));
        IntPtr library = NativeLibrary.Load(Path.Combine(root,"dist/FrameFlow/libmpv-2.dll"));
        IntPtr mediaInfo = NativeLibrary.Load(Path.Combine(root,"dist/FrameFlow/MediaInfo.dll"));
        NativeLibrary.SetDllImportResolver(typeof(MpvNet.Native.LibMpv).Assembly,(name,_,_) => name switch {
            "libmpv-2.dll" => library, "MediaInfo.dll" => mediaInfo, _ => IntPtr.Zero
        });
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Translator.Current = new WpfTranslator();
        App.Init(); Theme.Init();
        InstanceRouting.Initialize(Environment.ProcessPath!,App.ConfPath);
        using var host = new MainForm();
        Console.WriteLine($"TEST: controls PID {Environment.ProcessId}, profile {Player.ConfigFolder}");
        host.FormClosing += (_, e) => Console.WriteLine($"TEST: controls closing ({e.CloseReason})");
        object? Call(string name, params object[] values) => typeof(MainForm).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(host,values);
        T Field<T>(string name) => (T)typeof(MainForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;
        var checks = new List<string>();
        var samples = new List<object>();
        var keyEvents = new List<string[]>();
        Player.ClientMessage += args => { if (args.Length>0 && args[0]=="key-binding") lock(keyEvents) keyEvents.Add(args); };
        Exception? failure = null;
        void Check(bool condition,string message) {
            if (!condition) throw new Exception(message);
            checks.Add(message); Console.WriteLine("PASS: "+message);
        }
        async Task Until(Func<bool> condition,string message,int timeout=4000) {
            var clock=Stopwatch.StartNew();
            while (!condition()) {
                if (clock.ElapsedMilliseconds>timeout) throw new Exception("Timed out: "+message);
                await Task.Delay(20);
            }
        }
        async Task Seek(double time) {
            Player.CommandV("seek",time.ToString(System.Globalization.CultureInfo.InvariantCulture),"absolute+exact");
            await Until(()=>Math.Abs(Player.GetPropertyDouble("time-pos")-time)<.2 && !Player.GetPropertyBool("seeking"),"exact positioning");
        }
        JsonElement State(string name) {
            var value=JsonSerializer.Deserialize<JsonElement>(Player.GetPropertyString("user-data/modern/"+name));
            return value.ValueKind==JsonValueKind.String ? JsonSerializer.Deserialize<JsonElement>(value.GetString()!) : value;
        }
        void RenderSection(System.Windows.Controls.Border section,System.Windows.DpiScale dpi) {
            var size=new System.Windows.Size(section.ActualWidth,section.ActualHeight);
            var offset=System.Windows.Media.VisualTreeHelper.GetOffset(section);
            var visual=new System.Windows.Media.DrawingVisual();
            using(var drawing=visual.RenderOpen()) drawing.DrawRectangle(new System.Windows.Media.VisualBrush(section) {
                ViewboxUnits=System.Windows.Media.BrushMappingMode.Absolute,
                Viewbox=new System.Windows.Rect(offset.X,offset.Y,size.Width,size.Height), Stretch=System.Windows.Media.Stretch.Fill
            },null,new System.Windows.Rect(size));
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(size.Width*dpi.DpiScaleX),
                (int)Math.Ceiling(size.Height*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(output,section.Name+".png")))encoder.Save(file);
            Console.WriteLine($"Preview: {section.Name} {size} offset {offset}");
        }
        void CaptureHeader(bool requireAntialias) {
            var header=Field<Panel>("_modernHeader");
            using var raw=new System.Drawing.Bitmap(header.Width,header.Height);
            header.DrawToBitmap(raw,header.ClientRectangle);
            using var image=new System.Drawing.Bitmap(header.Width,header.Height);
            using(var graphics=System.Drawing.Graphics.FromImage(image)) {
                graphics.Clear(System.Drawing.Color.Black);
                // WM_PRINT does not consistently apply a window's native
                // region. Include that binary clip in the V23 baseline.
                if(header.Region!=null) graphics.SetClip(header.Region,System.Drawing.Drawing2D.CombineMode.Intersect);
                graphics.DrawImageUnscaled(raw,0,0);
            }
            image.Save(Path.Combine(output,"header.png"));
            int radius=(int)Math.Ceiling(10*header.DeviceDpi/96d);
            var counts=new List<int>();
            using var enlarged=new System.Drawing.Bitmap(radius*8,radius*8);
            using var previewGraphics=System.Drawing.Graphics.FromImage(enlarged);
            previewGraphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            previewGraphics.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
            for(int corner=0;corner<4;corner++) {
                int left=(corner%2==0)?0:image.Width-radius;
                int top=(corner<2)?0:image.Height-radius;
                int blended=0;
                for(int y=top;y<top+radius;y++)for(int x=left;x<left+radius;x++) {
                    var pixel=image.GetPixel(x,y);
                    if(pixel.R==0 && pixel.G>0 && pixel.G<150 && pixel.B>pixel.G && pixel.B<255)blended++;
                }
                counts.Add(blended);
                previewGraphics.DrawImage(image,new System.Drawing.Rectangle(corner%2*radius*4,corner/2*radius*4,radius*4,radius*4),
                    new System.Drawing.Rectangle(left,top,radius,radius),System.Drawing.GraphicsUnit.Pixel);
            }
            enlarged.Save(Path.Combine(output,"header-corners.png"));
            Console.WriteLine("Header corner intermediate-color pixels: "+string.Join(", ",counts));
            if(requireAntialias) Check(header.Region==null && counts.All(count=>count>=3),
                "all four actual header corners have antialiased color transitions without a binary native clip");
        }
        host.Shown += async (_,_) => {
            try {
                await Task.Delay(100);
                Player.CommandV("loadfile",media);
                await Until(()=>!Player.VideoSize.IsEmpty && Field<string>("_currentFile").Length>0,"video loaded");
                if(previewOnly) {
                    CaptureHeader(Environment.GetEnvironmentVariable("MPVNET_TEST_HEADER_AA")=="1");
                    Field<ModernSettings>("_modernSettings").ArrowNavigationSpeed=6;
                    Field<ModernSettings>("_modernSettings").TapCalculationMilliseconds=500;
                    var previewWindow=new ConfWindow("快捷键与鼠标:") { ShowActivated=false };
                    previewWindow.Show(); await Task.Delay(150);
                    var previewPanel=(System.Windows.Controls.StackPanel)previewWindow.FindName("MainStackPanel");
                    var input=previewPanel.Children.OfType<ModernPreferencesControl>().Single(card=>card.Setting.Directory=="快捷键与鼠标");
                    var previewDpi=System.Windows.Media.VisualTreeHelper.GetDpi(input);
                    foreach(var section in ((System.Windows.Controls.StackPanel)input.Content).Children.OfType<System.Windows.Controls.Border>())
                        RenderSection(section,previewDpi);
                    previewWindow.Close(); Check(true,"all three real settings sections rendered independently from the scroll viewport");
                    return;
                }
                Check(Player.GetPropertyString("autocreate-playlist")=="no" && Player.GetPropertyInt("playlist-count")==1,
                    "the front end prevents native directory expansion from reloading an explicit timestamp");
                string frontendName=Player.GetPropertyString("user-data/frontend/name");
                if(frontendName.StartsWith('"')) frontendName=JsonSerializer.Deserialize<string>(frontendName)!;
                Check(frontendName=="FrameFlow" &&
                    typeof(MainForm).Assembly.GetName().Name=="FrameFlow" && ModernVersion.Label=="V27",
                    $"the runtime frontend, WPF assembly and public version use FrameFlow V27 ({frontendName}, {typeof(MainForm).Assembly.GetName().Name}, {ModernVersion.Label})");
                Check(File.Exists(Path.Combine(config,"frameflow.conf")) && File.Exists(Path.Combine(config,"mpvnet.conf")) &&
                    File.ReadAllText(Path.Combine(config,"frameflow.conf"))==File.ReadAllText(Path.Combine(config,"mpvnet.conf")),
                    "legacy frontend configuration migrates to frameflow.conf without deleting the original");
                var defaultEscape=InputHelp.GetDefaults().Where(binding=>binding.Input.Equals("Esc",StringComparison.OrdinalIgnoreCase)).ToArray();
                Check(defaultEscape.Length==1 && defaultEscape[0].Command=="ignore",
                    "Escape has one explicit no-op default and cannot fall back to a native quit binding");
                var legacyEscapeFile=Path.Combine(config,"legacy-escape.conf");
                File.WriteAllText(legacyEscapeFile,"ESC quit #menu: File > Exit\nSpace cycle pause\n");
                var migratedEscape=new InputConf(legacyEscapeFile).GetContent();
                Check(InputHelp.Parse(migratedEscape).Single(binding=>binding.Input.Equals("Esc",StringComparison.OrdinalIgnoreCase)).Command is "" or "ignore",
                    "legacy menu-style input.conf loses its direct Escape quit binding");
                File.WriteAllText(legacyEscapeFile,"Esc quit\n");
                Check(InputHelp.Parse(new InputConf(legacyEscapeFile).GetContent()).Where(binding=>binding.Input.Equals("Esc",StringComparison.OrdinalIgnoreCase)).All(binding=>binding.Command is "" or "ignore"),
                    "legacy plain input.conf loses its Escape quit binding too");
                Check(InputHelp.RemoveEscapeQuitBinding("Esc async quit 0 # old\n").Contains("ignore # old") &&
                    InputHelp.RemoveEscapeQuitBinding("ESC quit-watch-later\n").Contains("ignore") &&
                    InputHelp.RemoveEscapeQuitBinding("Esc cycle pause\nShift+Esc quit\n")=="Esc cycle pause\nShift+Esc quit\n",
                    "Escape migration covers direct quit variants and retains unrelated custom commands and modifiers");
                var normalWindowBounds=host.Bounds;
                var roundedHeader=Field<RoundedPanel>("_modernHeader");
                void CheckSquareWindow(string label) {
                    Check(host.Region==null && host.Padding==Padding.Empty && !roundedHeader.RoundedCorners && roundedHeader.CornerRadius==0,
                        label+" has no outer region, resizing gutter or rounded header");
                    int result=DwmGetWindowAttribute(host.Handle,33,out int preference,4);
                    Check(result<0 || preference==1,label+" disables compositor corner rounding when supported");
                    using var image=new System.Drawing.Bitmap(roundedHeader.Width,roundedHeader.Height);
                    roundedHeader.DrawToBitmap(image,roundedHeader.ClientRectangle);
                    Check(new[] {new System.Drawing.Point(0,0),new System.Drawing.Point(image.Width-1,0),
                        new System.Drawing.Point(0,image.Height-1),new System.Drawing.Point(image.Width-1,image.Height-1)}
                        .All(point=>image.GetPixel(point.X,point.Y).ToArgb()==ModernTheme.HeaderBackground.ToArgb()),
                        label+" paints all four blue bar corners fully opaque");
                    image.Save(Path.Combine(output,label+"-header.png"));
                }
                Player.SetPropertyBool("pause",true);
                Player.CommandV("keypress","ESC"); await Task.Delay(100);
                Check(!host.IsDisposed && host.Visible && Player.Handle!=IntPtr.Zero,"Escape leaves normal playback open");
                host.WindowState=FormWindowState.Maximized; await Task.Delay(160);
                CheckSquareWindow("maximized");
                Player.CommandV("keypress","ESC"); await Task.Delay(80);
                Check(host.WindowState==FormWindowState.Maximized && !host.IsDisposed,"Escape does not restore or close a maximized player");
                Player.SetPropertyBool("fullscreen",true);
                await Until(()=>Field<bool>("_modernFullscreen"),"fullscreen for corner verification");
                Call("ShowFullscreenFooter");
                CheckSquareWindow("fullscreen");
                Player.CommandV("keypress","ESC"); await Task.Delay(80);
                Check(Field<bool>("_modernFullscreen") && !host.IsDisposed,"Escape has no newly introduced exit-fullscreen behavior");
                Player.SetPropertyBool("fullscreen",false);
                await Until(()=>!Field<bool>("_modernFullscreen"),"restore from fullscreen");
                CheckSquareWindow("restored-maximized");
                host.WindowState=FormWindowState.Normal; host.Bounds=normalWindowBounds; await Task.Delay(160);
                Check(host.Region!=null && !host.Region.IsVisible(0,0) && roundedHeader.RoundedCorners && roundedHeader.CornerRadius==10,
                    "restoring a normal window reinstates outer and antialiased header rounding");
                CaptureHeader(true);
                var footer=Field<Panel>("_modernFooter");
                using (var toolbarPreview=new System.Drawing.Bitmap(footer.Width,footer.Height)) {
                    footer.DrawToBitmap(toolbarPreview,footer.ClientRectangle);
                    toolbarPreview.Save(Path.Combine(output,"normal-toolbar.png"));
                }
                Check(Field<ModernSettings>("_modernSettings").Toolbar.Select(item=>item.Id).SequenceEqual(new[] {
                    "frame-back","frame-forward","screenshot","bookmark","ab-loop","timestamp" }),
                    "the normal toolbar has exactly one previous-frame and next-frame button, with six default tools");
                Player.SetPropertyBool("pause",true);
                await Seek(60);
                var surface=Field<VideoSurface>("_videoSurface");
                surface.Focus();
                var rightDown=Message.Create(surface.Handle,0x0100,(IntPtr)Keys.Right,(IntPtr)0x004D0001);
                Check(!surface.PreProcessMessage(ref rightDown),"the physical Right key reaches the video input instead of changing control focus");
                foreach (var key in new[] { Keys.Left, Keys.Right, Keys.Up, Keys.Down }) {
                    var message=Message.Create(surface.Handle,0x0100,(IntPtr)key,IntPtr.Zero);
                    Check(!surface.PreProcessMessage(ref message),"video surface keeps "+key+" as player input");
                }
                var formRight=Message.Create(host.Handle,0x0100,(IntPtr)Keys.Right,IntPtr.Zero);
                Check(!host.PreProcessMessage(ref formRight),"a direction key on the main form is not dialog navigation");
                void VideoKey(Keys key,bool down,bool repeat=false) {
                    uint scan=key==Keys.Right ? 0x4du : key==Keys.Left ? 0x4bu : 0x0fu;
                    uint flags=(scan<<16)|(key==Keys.Tab ? 1u : 0x01000001u)|(down ? repeat ? 0x40000000u : 0u : 0xc0000000u);
                    var message=Message.Create(surface.Handle,down ? 0x0100 : 0x0101,(IntPtr)key,new IntPtr(unchecked((int)flags)));
                    if (down && surface.PreProcessMessage(ref message)) throw new Exception("WinForms consumed "+key);
                    // Invoke this process's own control's normal message handler.
                    typeof(VideoSurface).GetMethod("WndProc",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(surface,new object[] { message });
                }
                var seek=Field<ModernSeek>("_modernSeek");
                bool LiveProgress(string action) {
                    var feedback=State("navigation");
                    return feedback.GetProperty("visible").GetBoolean() && feedback.GetProperty("action").GetString()==action &&
                        feedback.GetProperty("kind").GetString()=="navigation" &&
                        Math.Abs(feedback.GetProperty("time").GetDouble()-seek.Position)<.001 &&
                        Math.Abs(feedback.GetProperty("percent").GetDouble()-seek.Position/seek.Duration*100)<.001 &&
                        Math.Abs(TimeSpan.Parse(Field<Label>("_modernTime").Text).TotalSeconds-seek.Position)<1.1 &&
                        feedback.GetProperty("text").GetString()!.Contains("%") &&
                        Field<string>("_navigationRendered").Contains(feedback.GetProperty("timestamp").GetString()!);
                }
                var playButton=Field<ModernButton>("_modernPlay");
                playButton.Focus(); playButton.PerformClick();
                Check(surface.Focused,"a normal toolbar click returns playback-key focus to the video surface");
                await Until(()=>!Player.GetPropertyBool("pause"),"toolbar play click");
                Player.SetPropertyBool("pause",true);
                var speedButton=Field<ModernButton>("_modernSpeed");
                speedButton.Focus(); speedButton.PerformClick();
                Check(Field<ToolStripDropDown>("_speedPopup").Visible,"the focus fix leaves the speed editing popup open");
                var speedPopup=Field<ToolStripDropDown>("_speedPopup");
                typeof(ToolStripDropDown).GetMethod("ProcessDialogKey",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(speedPopup,new object[] {Keys.Escape});
                Check(!speedPopup.Visible && !host.IsDisposed,"Escape still closes the speed popup without exiting playback");
                var volumeButton=Field<ModernButton>("_modernVolume");
                volumeButton.Focus(); volumeButton.PerformClick();
                Check(Field<ToolStripDropDown>("_volumePopup").Visible,"the focus fix leaves the volume editing popup open");
                var volumePopup=Field<ToolStripDropDown>("_volumePopup");
                typeof(ToolStripDropDown).GetMethod("ProcessDialogKey",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(volumePopup,new object[] {Keys.Escape});
                Check(!volumePopup.Visible && !host.IsDisposed,"Escape still closes the volume popup without exiting playback");
                surface.Focus();
                Check(ShortcutCatalog.All.All(action=>action.Command!="seek 5" && action.Command!="seek -5"),"no fixed 5-second action in the common shortcut page");
                Check(ShortcutCatalog.All.Count==26 && InputHelp.GetDefaults().All(binding=>binding.Command!="seek 30" && binding.Command!="seek -30"),
                    "30-second forward/back actions are absent from common shortcuts and default menus");
                await Until(()=>Field<bool>("_contextMenuIsReady"),"right-click menu ready");
                var resetViewAction=ShortcutCatalog.Find("zoom-reset")!;
                Call("SetModernShortcut",resetViewAction,"Ctrl+F10",false);
                var playerMenu=(System.Windows.Controls.ContextMenu)typeof(MainForm).GetProperty("ContextMenu",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;
                playerMenu.IsOpen=true; await Task.Delay(60);
                var escapeMessage=Message.Create(surface.Handle,0x0100,(IntPtr)Keys.Escape,IntPtr.Zero);
                typeof(VideoSurface).GetMethod("WndProc",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(surface,new object[] {escapeMessage});
                Check(!playerMenu.IsOpen && !host.IsDisposed,"Escape still dismisses the right-click menu without closing playback");
                var resetViewMenu=playerMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item=>item.Header?.ToString()=="还原初始画面大小和位置");
                Check(resetViewMenu.InputGestureText.Length==0 && playerMenu.Items.OfType<System.Windows.Controls.MenuItem>().All(item=>item.InputGestureText.Length==0),
                    "main right-click menu contains no stale keyboard hints, including after remapping view reset");
                Call("SetModernShortcut",resetViewAction,"",true);
                double beforeUpDown=Player.GetPropertyDouble("time-pos");
                Player.CommandV("keypress","Up"); Player.CommandV("keypress","Down"); await Task.Delay(80);
                Check(Math.Abs(Player.GetPropertyDouble("time-pos")-beforeUpDown)<.05,"default Up/Down do not jump 30 seconds");
                string originalInput=Player.UsedInputConfContent;
                Player.UsedInputConfContent="Up seek 30\nDown seek -30\nLeft seek -5\nRight seek 5\n";
                Call("InitializeModernShortcuts");
                Player.CommandV("keypress","Up"); Player.CommandV("keypress","Down"); await Task.Delay(80);
                Check(Math.Abs(Player.GetPropertyDouble("time-pos")-beforeUpDown)<.05,"legacy plain 30-second arrow defaults are disabled at runtime");
                Player.UsedInputConfContent=originalInput; Call("InitializeModernShortcuts");
                var pause=ShortcutCatalog.Find("pause")!;
                var mute=ShortcutCatalog.Find("mute")!;
                Check(Call("SetModernShortcut",pause,"Ctrl+F9",false)==null,"recording an unused shortcut succeeds");
                Check(Call("SetModernShortcut",mute,"Ctrl+F9",false) is string,"a conflicting shortcut is rejected without replacing the first");
                Player.CommandV("keypress","Ctrl+F9");
                await Until(()=>!Player.Paused,"custom shortcut executes");
                Player.SetPropertyBool("pause",true);
                Check(Call("SetModernShortcut",pause,"",false)==null,"clearing a shortcut succeeds");
                Player.CommandV("keypress","Ctrl+F9"); await Task.Delay(100);
                Check(Player.Paused,"cleared custom shortcut stops executing");
                Check(Call("SetModernShortcut",pause,"",true)==null,"restore returns to the original binding");
                Call("SetModernMouseAction","MBTN_Mid","cycle mute");
                bool muted=Player.GetPropertyBool("mute");
                Player.CommandV("keypress","MBTN_Mid");
                await Until(()=>Player.GetPropertyBool("mute")!=muted,"mouse binding executes");
                var saved=ModernSettings.Load(Path.Combine(config,"modern-ui.json"));
                Check(saved.MouseActions.GetValueOrDefault("MBTN_Mid")=="cycle mute" && !saved.ShortcutOverrides.ContainsKey("pause"),"mouse and restored key settings persist without changing input.conf");
                Check(!File.Exists(Path.Combine(config,"input.conf")),"shortcut edits do not rewrite the user's input.conf");

                await Seek(60);
                Player.SetPropertyDouble("speed",1.25);
                await Task.Delay(100);
                VideoKey(Keys.Right,true);
                await Until(()=>Field<int>("_heldDirection")==1,"right key-down is delivered");
                await Until(()=>Player.GetPropertyDouble("speed")>4,"continuous forward playback begins");
                double previous=Player.GetPropertyDouble("time-pos");
                int advances=0, forwardUiUpdates=0;
                double previousThumb=seek.Position;
                for (int i=0;i<9;i++) {
                    await Task.Delay(85);
                    double position=Player.GetPropertyDouble("time-pos");
                    samples.Add(new { direction="forward",position,thumb=seek.Position,clock=Field<Label>("_modernTime").Text,feedback=State("navigation"),speed=Player.GetPropertyDouble("speed") });
                    if (position>previous+.05) advances++;
                    if (seek.Position>previousThumb+.05 && LiveProgress("连续快进")) forwardUiUpdates++;
                    previousThumb=seek.Position;
                    previous=position;
                }
                VideoKey(Keys.Right,false);
                await Until(()=>Field<int>("_heldDirection")==0 && Player.Paused && Math.Abs(Player.GetPropertyDouble("speed")-1.25)<.001,"forward key release restores state");
                Check(advances>=7 && previous>63 && previous<67,"holding Right advances continuous playback across multiple frames");
                Check(forwardUiUpdates>=7,"held Right continuously updates the visible progress, clock and top-left timestamp/percentage together");
                await Task.Delay(120);
                samples.Add(new { direction="forward-release", feedback=State("navigation"),
                    held=Field<int>("_heldDirection"), paused=Player.Paused, speed=Player.GetPropertyDouble("speed"),
                    focused=surface.Focused });
                Check(State("navigation").GetProperty("action").GetString()=="快进","restored speed/pause notifications do not overwrite the release timestamp");
                double releasedPosition=Player.GetPropertyDouble("time-pos");
                typeof(MainForm).GetField("_navigationLastPaused",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(host,false);
                Call("ObserveNavigationPosition",releasedPosition-1/30d);
                Call("ObserveNavigationPosition",releasedPosition);
                await Task.Delay(100);
                Check(State("navigation").GetProperty("action").GetString()=="快进",
                    "queued pre-pause positions cannot misclassify the held release as a new frame step");
                Player.CommandV("frame-step");
                await Until(()=>State("navigation").GetProperty("action").GetString()=="下一帧" &&
                    Player.GetPropertyDouble("time-pos")>releasedPosition+.01,"real native frame step after hold");
                Check(true,"an actual native frame step immediately after the held release still displays its own feedback");
                Check(Field<ModernSettings>("_modernSettings").Speed==1.25m,"temporary fast-forward speed never becomes the saved speed");

                await Seek(90);
                VideoKey(Keys.Left,true);
                await Until(()=>Field<int>("_heldDirection")==-1 && Field<bool>("_heldStarted"),"continuous rewind begins");
                previous=Player.GetPropertyDouble("time-pos"); int reverses=0, backwardUiUpdates=0;
                previousThumb=seek.Position;
                for (int i=0;i<10;i++) {
                    await Task.Delay(85);
                    double position=Player.GetPropertyDouble("time-pos");
                    samples.Add(new { direction="backward",position,thumb=seek.Position,clock=Field<Label>("_modernTime").Text,feedback=State("navigation"),seeking=Player.GetPropertyBool("seeking") });
                    if (position<previous-.04) reverses++;
                    if (seek.Position<previousThumb-.04 && LiveProgress("连续快退")) backwardUiUpdates++;
                    previousThumb=seek.Position;
                    previous=position;
                }
                VideoKey(Keys.Left,false);
                await Until(()=>Field<int>("_heldDirection")==0 && !Player.GetPropertyBool("seeking"),"rewind release settles");
                Check(reverses>=7 && previous>84 && previous<89,"holding Left displays successive earlier frames instead of 5-second jumps");
                Check(backwardUiUpdates>=7,"held Left updates the progress and live top-left timestamp as decoded frames move backward");
                await Until(()=>State("navigation").GetProperty("action").GetString()=="快退","final reverse timestamp");
                Check(Math.Abs(State("navigation").GetProperty("time").GetDouble()-Player.GetPropertyDouble("time-pos"))<.05,
                    "reverse key-up feedback shows the settled final frame rather than an outgoing seek position");
                Check(Player.Paused && Math.Abs(Player.GetPropertyDouble("speed")-1.25)<.001,"rewind release restores original pause and speed");
                await Seek(120);
                Call("SetModernFullscreen",true);
                surface.Focus(); VideoKey(Keys.Right,true);
                await Until(()=>Field<bool>("_heldStarted"),"long forward hold begins");
                int longUiUpdates=0;
                for (int i=0;i<40;i++) {
                    await Task.Delay(85);
                    if (LiveProgress("连续快进")) longUiUpdates++;
                    if (i%5==0) VideoKey(Keys.Right,true,true);
                }
                Check(longUiUpdates>=36 && LiveProgress("连续快进"),"a hold longer than three seconds keeps refreshing the visible timestamp and percentage");
                Check(footer.Visible,"fullscreen progress controls remain visible throughout a long hold");
                Player.CommandV("screenshot-to-file",Path.Combine(output,"held-forward-osd.png"),"window");
                VideoKey(Keys.Right,false);
                await Until(()=>Field<int>("_heldDirection")==0 && Player.GetPropertyBool("pause"),"long hold ends");
                await Task.Delay(120);
                Check(State("navigation").GetProperty("visible").GetBoolean() && State("navigation").GetProperty("action").GetString()=="快进",
                    "key-up leaves the final timestamp visible");
                await Task.Delay(2500);
                Check(State("navigation").GetProperty("visible").GetBoolean(),"the final timestamp remains visible near the end of its three-second lifetime");
                await Until(()=>!State("navigation").GetProperty("visible").GetBoolean(),"final feedback expires after three seconds",800);
                Check(true,"the final held-navigation hint expires after key release");
                Call("SetModernFullscreen",false);
                Field<ModernSettings>("_modernSettings").ShowNavigationFeedback=false;
                Call("ClearNavigationFeedback");
                VideoKey(Keys.Right,true);
                await Until(()=>Field<bool>("_heldStarted"),"hold with navigation hints disabled");
                await Task.Delay(120);
                Check(!State("navigation").GetProperty("visible").GetBoolean(),"held navigation respects the disabled progress-hint setting");
                Player.SetPropertyDouble("volume",Player.GetPropertyDouble("volume")-2);
                await Until(()=>State("navigation").GetProperty("visible").GetBoolean() && State("navigation").GetProperty("action").GetString()=="volume",
                    "volume hint during held navigation");
                await Until(()=>!State("navigation").GetProperty("visible").GetBoolean(),"volume hint expires during hold",3400);
                Check(true,"a volume hint still expires after three seconds while a navigation key remains held");
                VideoKey(Keys.Right,false);
                await Until(()=>Field<int>("_heldDirection")==0 && Player.GetPropertyBool("pause"),"hold with disabled hints ends");
                Field<ModernSettings>("_modernSettings").ShowNavigationFeedback=true;
                await Seek(90);
                VideoKey(Keys.Right,true);
                await Until(()=>Field<int>("_heldDirection")==1,"tap down delivered");
                VideoKey(Keys.Right,false);
                await Until(()=>Field<int>("_heldDirection")==0 && Player.GetPropertyDouble("time-pos")>90.05,"tap completes");
                Check(Player.GetPropertyDouble("time-pos")<90.3,"a short tap makes a small step, never a 5-second jump");
                Player.CommandV("keydown","Right");
                await Until(()=>Field<bool>("_heldStarted"),"hold before focus loss");
                Call("OnDeactivate",EventArgs.Empty);
                Check(Field<int>("_heldDirection")==0 && Player.GetPropertyBool("pause") && Math.Abs(Player.GetPropertyDouble("speed")-1.25)<.001,"focus loss immediately ends a held navigation and restores state");
                Player.CommandV("keyup","Right");

                var navigationSettings=Field<ModernSettings>("_modernSettings");
                navigationSettings.ArrowNavigationSpeed=4; navigationSettings.TapCalculationMilliseconds=100;
                Call("SetSpeed",2m); Call("FlushSliderProperties");
                await Until(()=>Math.Abs(Player.GetPropertyDouble("speed")-2)<.001,"normal speed changes independently");
                await Seek(60); surface.Focus();
                VideoKey(Keys.Right,true);
                await Until(()=>Field<bool>("_heldStarted"),"absolute arrow rate begins");
                Check(Player.GetPropertyDouble("speed")==4 && Field<double>("_heldRate")==4,
                    "the direction rate is exactly 4x when normal playback is 2x, never multiplied");
                VideoKey(Keys.Right,false);
                await Until(()=>Player.Paused && Player.GetPropertyDouble("speed")==2,"arrow restores independent normal speed");
                await Seek(90); VideoKey(Keys.Left,true);
                await Until(()=>Field<bool>("_heldStarted"),"absolute reverse rate begins");
                Check(Field<double>("_heldRate")==4,"forward and reverse share one absolute direction rate");
                await Task.Delay(180); VideoKey(Keys.Left,false);
                await Until(()=>Field<int>("_heldDirection")==0 && !Player.GetPropertyBool("seeking") &&
                    State("navigation").GetProperty("action").GetString()=="快退",
                    "reverse key-up and its final queued seek settle before configured taps");
                foreach (var (key,sign) in new[]{(Keys.Right,1),(Keys.Left,-1)}) {
                    await Seek(90); VideoKey(key,true);
                    await Until(()=>Field<int>("_heldDirection")==sign,"configured tap starts");
                    await Task.Delay(45); VideoKey(key,false);
                    await Until(()=>!Player.GetPropertyBool("seeking") && Math.Abs(Player.GetPropertyDouble("time-pos")-(90+sign*.4))<.06,"configured tap finishes");
                    Check(Math.Abs(Player.GetPropertyDouble("time-pos")-(90+sign*.4))<.06,
                        key+" tap uses shared 4x times independently configured 100ms = 0.4 seconds");
                }
                navigationSettings.TapCalculationMilliseconds=1000;
                // Timing/settings checks use mpv's explicit input API so the
                // user's physical modifier state cannot change a synthetic
                // Win32 key. The video-surface message route is covered above.
                var thresholdClock=Stopwatch.StartNew(); Player.CommandV("keydown","Right");
                await Until(()=>Field<bool>("_heldStarted"),"V21 threshold independent from tap duration");
                Check(thresholdClock.ElapsedMilliseconds>=150 && thresholdClock.ElapsedMilliseconds<500 &&
                    State("held-navigation").GetProperty("thresholdMilliseconds").GetInt32()==180,
                    "a 1000ms tap calculation does not change the approximately 180ms V21 hold classification");
                Player.CommandV("keyup","Right");
                await Until(()=>Field<int>("_heldDirection")==0 && Player.Paused,"arrow key settles before Tab");
                navigationSettings.TemporaryFastForwardSpeed=3;
                await Seek(90); VideoKey(Keys.Tab,true);
                await Until(()=>Field<bool>("_heldStarted"),"independent Tab hold begins");
                Check(Player.GetPropertyDouble("speed")==3 && State("held-navigation").GetProperty("source").GetString()=="temporary-shortcut",
                    "the physical Tab hold uses its own fixed 3x rate despite normal 2x and arrow 4x");
                await Task.Delay(200); VideoKey(Keys.Tab,false);
                await Until(()=>Field<int>("_heldDirection")==0 && Player.Paused && Player.GetPropertyDouble("speed")==2,"Tab restores normal state");
                Check(navigationSettings.Speed==2,"the independent hold never overwrites saved normal playback speed");
                await Seek(90); VideoKey(Keys.Tab,true);
                await Until(()=>Field<int>("_heldDirection")==2,"short Tab starts");
                VideoKey(Keys.Tab,false); await Task.Delay(80);
                Check(Math.Abs(Player.GetPropertyDouble("time-pos")-90)<.03,"a short independent-key tap does not trigger an arrow step");
                Call("SetSpeed",1.25m); Call("FlushSliderProperties");
                navigationSettings.ArrowNavigationSpeed=5; navigationSettings.TapCalculationMilliseconds=24;

                void Wheel(int delta,bool ctrl=false) {
                    var pointer=surface.PointToScreen(new System.Drawing.Point(surface.Width/2,surface.Height/2));
                    int coordinates=(pointer.X&0xffff)|((pointer.Y&0xffff)<<16);
                    int flags=((delta&0xffff)<<16)|(ctrl?8:0);
                    var message=Message.Create(surface.Handle,0x020a,new IntPtr(flags),new IntPtr(coordinates));
                    typeof(VideoSurface).GetMethod("WndProc",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(surface,new object[]{message});
                }
                Player.SetPropertyDouble("volume",50); Call("ResetVideoView",false);
                navigationSettings.WheelAction="volume"; navigationSettings.CtrlWheelAction="zoom"; navigationSettings.CtrlMouseVideoView=false;
                Call("SaveModernPreferences"); Wheel(120);
                await Until(()=>Player.GetPropertyDouble("volume")==52,"default wheel adjusts volume once");
                Check(Player.GetPropertyDouble("volume")==52,"a physical wheel message adjusts volume once without duplicate native processing");
                Wheel(120,true);
                await Until(()=>Math.Abs(Player.GetPropertyDouble("video-zoom")-.1)<.001,"Ctrl zoom remains independent of dragging");
                Check(Player.GetPropertyDouble("volume")==52 && !navigationSettings.CtrlMouseVideoView,
                    "Ctrl wheel can zoom with Ctrl dragging disabled and does not also change volume");
                navigationSettings.WheelAction="zoom"; navigationSettings.CtrlWheelAction="volume";
                Call("SaveModernPreferences"); Wheel(120);
                await Until(()=>Math.Abs(Player.GetPropertyDouble("video-zoom")-.2)<.001,"ordinary wheel is reassigned to zoom");
                Wheel(120,true); await Until(()=>Player.GetPropertyDouble("volume")==54,"Ctrl wheel is reassigned to volume");
                Check(Math.Abs(Player.GetPropertyDouble("video-zoom")-.2)<.001 && Player.GetPropertyDouble("volume")==54,
                    "ordinary and Ctrl wheel actions can swap independently, rather than being hardcoded");
                navigationSettings.WheelAction="speed"; navigationSettings.CtrlWheelAction="none"; Call("SaveModernPreferences");
                Wheel(120); Call("FlushSliderProperties");
                await Until(()=>Math.Abs(Player.GetPropertyDouble("speed")-1.30)<.001,"wheel uses normal-speed step");
                Wheel(120,true); await Task.Delay(60);
                Check(Math.Abs(Player.GetPropertyDouble("speed")-1.30)<.001 && Player.GetPropertyDouble("volume")==54,
                    "wheel speed uses its normal playback step and a disabled Ctrl wheel performs no action");
                navigationSettings.WheelAction="seek"; navigationSettings.TapCalculationMilliseconds=100; navigationSettings.ArrowNavigationSpeed=4;
                Call("SaveModernPreferences"); await Seek(90); Wheel(120);
                await Until(()=>!Player.GetPropertyBool("seeking") && Math.Abs(Player.GetPropertyDouble("time-pos")-90.4)<.06,"wheel configured navigation");
                Check(Math.Abs(Player.GetPropertyDouble("time-pos")-90.4)<.06,"wheel navigation uses the configured tap distance without reintroducing 5/30 second jumps");
                navigationSettings.WheelAction="input-conf"; navigationSettings.CtrlWheelAction="input-conf"; Call("SaveModernPreferences");
                Player.CommandV("keybind","WHEEL_UP","add volume 9");
                Player.CommandV("keybind","WHEEL_DOWN","add volume -7");
                Player.CommandV("keybind","Ctrl+WHEEL_UP","add volume 11");
                Player.SetPropertyDouble("volume",40); Wheel(60); Wheel(60,true);
                Check(Player.GetPropertyDouble("volume")==40,"high-resolution half-notches accumulate independently for ordinary and Ctrl bindings");
                Wheel(60); await Until(()=>Player.GetPropertyDouble("volume")==49,"custom ordinary native binding");
                Wheel(60,true); await Until(()=>Player.GetPropertyDouble("volume")==60,"custom Ctrl native binding");
                Wheel(-120); await Until(()=>Player.GetPropertyDouble("volume")==53,"custom downward native binding");
                Check(Player.GetPropertyDouble("volume")==53,"follow-configuration dispatches all three custom native wheel bindings without frontend overrides");
                navigationSettings.WheelAction="none"; Call("SaveModernPreferences"); Wheel(120); await Task.Delay(60);
                Check(Player.GetPropertyDouble("volume")==53,"disabled ordinary wheel does not fall through to a configured native volume action");
                foreach (var key in new[]{"WHEEL_UP","WHEEL_DOWN","Ctrl+WHEEL_UP"})
                    Player.CommandV("keybind",key,Field<Dictionary<string,string>>("_shortcutBase").GetValueOrDefault(key,"ignore"));
                navigationSettings.WheelAction="volume"; navigationSettings.MouseWheelVolume=true; navigationSettings.CtrlWheelAction="zoom"; navigationSettings.CtrlMouseVideoView=true;
                navigationSettings.ArrowNavigationSpeed=5; navigationSettings.TapCalculationMilliseconds=24;
                Call("SetSpeed",1.25m); Call("FlushSliderProperties"); Call("ResetVideoView",false); Call("SaveModernPreferences");

                void Mouse(string method,int x) => typeof(ModernSeek).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(seek,new object[] { new MouseEventArgs(MouseButtons.Left,1,x,seek.Height/2,0) });
                int previews=Field<int>("_seekPreviewCommands"), commits=Field<int>("_seekCommits");
                Mouse("OnMouseDown",50);
                for (int group=0;group<8;group++) {
                    for (int i=0;i<20;i++) Mouse("OnMouseMove",80+group*24+i);
                    double thumb=seek.Position;
                    Call("RefreshModernState");
                    Check(seek.IsDragging && seek.Position==thumb,"the progress refresh preserves the pointer position while dragging ("+group+")");
                    await Task.Delay(35);
                }
                Mouse("OnMouseMove",seek.Width/3);
                double final=seek.Position;
                Mouse("OnMouseUp",seek.Width/3);
                await Until(()=>Math.Abs(Player.GetPropertyDouble("time-pos")-final)<.5 && !Player.GetPropertyBool("seeking"),"final drag target",7000);
                Check(Field<int>("_seekCommits")==commits+1,"one release sends exactly one accurate final seek");
                Check(Field<int>("_seekPreviewCommands")-previews<12,"160 pointer changes coalesce into at most 11 decoder previews");
                Call("NavigateVideo","定位",new string[] {"seek","120","absolute+exact"});
                await Until(()=>Math.Abs(Player.GetPropertyDouble("time-pos")-120)<.5,"navigation after drag");
                Call("RefreshModernState");
                Check(Math.Abs(seek.Position-120)<.5,"new navigation supersedes the released drag target");
                for (int i=0;i<100;i++) { Call("QueueSliderProperty","volume",i+10d); Call("SetSpeed",1+i/100m); }
                Call("FlushSliderProperties");
                await Until(()=>Math.Abs(Player.GetPropertyDouble("volume")-100)<.01 && Math.Abs(Player.GetPropertyDouble("speed")-1.99)<.001,"latest slider targets");
                Check(Player.GetPropertyDouble("volume")==100 && Math.Abs(Player.GetPropertyDouble("speed")-1.99)<.001,"rapid volume/speed drags apply the latest values and respect the volume cap");
                Call("SaveModernSettings");
                Check(ModernSettings.Load(Path.Combine(config,"modern-ui.json")).Speed==1.99m,"final slider speed is saved");
                var modernSettings=Field<ModernSettings>("_modernSettings");
                modernSettings.VolumeBoost=true; Call("ApplyModernVolumeOptions");
                Check(Player.GetPropertyDouble("volume")==100 && Player.GetPropertyDouble("volume-max")==300,
                    "enabling volume enhancement raises the ceiling without raising the current volume");
                Call("QueueSliderProperty","volume",300d);
                modernSettings.VolumeBoost=false; Call("ApplyModernVolumeOptions"); Call("FlushSliderProperties");
                await Task.Delay(70);
                Check(Player.GetPropertyDouble("volume")==100,"disabling enhancement also clamps a not-yet-submitted slider update");
                Player.CommandV("keydown","Left");
                await Until(()=>Field<bool>("_heldStarted"),"rewind before new file");
                Player.CommandV("loadfile",media,"replace","-1","start=150");
                await Until(()=>Field<int>("_heldDirection")==0 && Math.Abs(Player.GetPropertyDouble("time-pos")-150)<.5 && !Player.GetPropertyBool("seeking"),
                    "new file's explicit timestamp supersedes a held rewind",7000);
                Player.CommandV("keyup","Left");
                Check(Math.Abs(Player.GetPropertyDouble("time-pos")-150)<.5,"a new explicit video timestamp cancels the old held navigation");
                modernSettings.AutoFolderPlaylist=true;
                Player.CommandV("loadfile",media,"replace","-1","start=180");
                await Until(()=>Player.GetPropertyInt("playlist-count")>1 && Math.Abs(Player.GetPropertyDouble("time-pos")-180)<.5 && !Player.GetPropertyBool("seeking"),
                    "front-end folder expansion preserves the explicit start",7000);
                await Task.Delay(300);
                Check(Math.Abs(Player.GetPropertyDouble("time-pos")-180)<.5,"appending same-directory videos preserves the current file and its explicit time");
                var preferences = new ConfWindow("快捷键与鼠标:") { ShowActivated=false };
                preferences.Show();
                await Task.Delay(100);
                var migratedModels=(List<Setting>)typeof(ConfWindow).GetField("_settings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preferences)!;
                Check(migratedModels.Single(setting=>setting.Name=="process-instance").Value=="multi",
                    "renamed frameflow.conf still loads frontend values into the settings schema");
                Check(SettingSearch.Matches(migratedModels.Single(setting=>setting.Name=="process-instance"),"FrameFlow"),
                    "the frontend-settings search uses the FrameFlow name with its compatible legacy schema");
                void SettingsEscape() {
                    var key=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                        System.Windows.PresentationSource.FromVisual(preferences),Environment.TickCount,System.Windows.Input.Key.Escape) {
                        RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent };
                    preferences.RaiseEvent(key);
                }
                preferences.SearchText="倍率";
                SettingsEscape();
                Check(preferences.SearchText.Length==0 && preferences.IsVisible && !host.IsDisposed,
                    "Escape clears settings search without closing settings or playback");
                preferences.NavigateTo("快捷键与鼠标:");
                var panel=(System.Windows.Controls.StackPanel)preferences.FindName("MainStackPanel");
                var shortcutCard=panel.Children.OfType<ShortcutSettingsControl>().Single();
                Check(shortcutCard.Visibility==System.Windows.Visibility.Visible && shortcutCard.SearchableSettings.Count==26,
                    "the unified settings page exposes 26 common actions including picture and night mode");
                var content=(System.Windows.FrameworkElement)preferences.Content;
                var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(content);
                var preview=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*dpi.DpiScaleX),
                    (int)Math.Ceiling(content.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,System.Windows.Media.PixelFormats.Pbgra32);
                preview.Render(content);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
                using (var previewFile=File.Create(Path.Combine(output,"shortcut-page.png"))) encoder.Save(previewFile);
                preferences.SearchText="连续快进";
                await Task.Delay(60);
                var settingsState=JsonSerializer.Deserialize<JsonElement>(Player.GetPropertyString("user-data/modern/settings"));
                if (settingsState.ValueKind==JsonValueKind.String) settingsState=JsonSerializer.Deserialize<JsonElement>(settingsState.GetString()!);
                Check(settingsState.GetProperty("results").GetProperty("count").GetInt32()==1 &&
                    settingsState.GetProperty("results").GetProperty("current").GetString()=="shortcut.hold-forward",
                    "global search opens the correct single shortcut result");
                preferences.NavigateTo("快捷键与鼠标:");
                Check(panel.Children.OfType<ModernPreferencesControl>().Any(control=>control.Visibility==System.Windows.Visibility.Visible &&
                    control.Setting.Directory=="快捷键与鼠标"),"mouse preferences appear with keyboard shortcuts on the same page");

                var inputCard=panel.Children.OfType<ModernPreferencesControl>().Single(control=>control.Setting.Directory=="快捷键与鼠标");
                IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent) {
                    yield return parent;
                    for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++)
                        foreach(var item in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(parent,i))) yield return item;
                }
                System.Windows.Controls.TextBox Editor(string title) => Descendants(inputCard).OfType<System.Windows.Controls.TextBox>()
                    .Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)==title);
                var sections=Descendants(inputCard).OfType<System.Windows.Controls.Border>().Where(border=>border.Name is "NavigationSection" or "MouseSection" or "FeedbackSection").ToArray();
                var mouseSection=sections.Single(section=>section.Name=="MouseSection");
                var feedbackSection=sections.Single(section=>section.Name=="FeedbackSection");
                Check(sections.Length==3 && !Descendants(mouseSection).OfType<System.Windows.Controls.CheckBox>().Any() &&
                    Descendants(feedbackSection).OfType<System.Windows.Controls.ComboBox>().Any(),
                    "navigation, uniform mouse-action rows and feedback form three separate settings sections");
                var ctrlRight=Descendants(mouseSection).OfType<System.Windows.Controls.ComboBox>().Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)=="Ctrl＋右键单击");
                ctrlRight.SelectedItem=((IEnumerable<DoubleClickAction>)ctrlRight.ItemsSource).Single(action=>action.Id=="cycle mute");
                bool beforeCtrlRight=Player.GetPropertyBool("mute"); Player.CommandV("keypress","Ctrl+MBTN_Right");
                await Until(()=>Player.GetPropertyBool("mute")!=beforeCtrlRight,"Ctrl right action works");
                Check(modernSettings.MouseActions.GetValueOrDefault("Ctrl+MBTN_Right")=="cycle mute",
                    "the new Ctrl right action row saves and executes its selected command");
                ctrlRight.SelectedItem=((IEnumerable<DoubleClickAction>)ctrlRight.ItemsSource).Single(action=>action.Id=="input-conf");
                Check(!modernSettings.MouseActions.ContainsKey("Ctrl+MBTN_Right"),"follow-configuration restores the original modified mouse binding");
                var ctrlDrag=Descendants(mouseSection).OfType<System.Windows.Controls.ComboBox>().Single(box=>System.Windows.Automation.AutomationProperties.GetName(box)=="Ctrl＋左键拖动");
                ctrlDrag.SelectedItem=MouseDragActions.All.Single(action=>action.Id=="none");
                Check(modernSettings.CtrlDragAction=="none" && !modernSettings.CtrlMouseVideoView && modernSettings.CtrlWheelAction=="zoom",
                    "disabled Ctrl left dragging is independent from Ctrl wheel zoom");
                bool DragInput(int kind,int x,int flags) {
                    var message=Message.Create(surface.Handle,kind,new IntPtr(flags),new IntPtr((x&0xffff)|((surface.Height/2&0xffff)<<16)));
                    return (bool)Call("HandleVideoViewInput",surface,message)!;
                }
                Call("ResetVideoView",false);
                bool stoppedDown=DragInput(0x0201,surface.Width/2,9), stoppedMove=DragInput(0x0200,surface.Width/2+20,9), stoppedUp=DragInput(0x0202,surface.Width/2+20,0);
                Check(stoppedDown&&stoppedMove&&stoppedUp&&!surface.Capture&&Player.GetPropertyDouble("video-pan-x")==0,
                    "no-action Ctrl drag consumes the whole gesture and releases capture even when Ctrl is released first");
                ctrlDrag.SelectedItem=MouseDragActions.All.Single(action=>action.Id=="pan");
                Editor("方向键快进 / 快退倍率（×）").Text="4.00";
                Editor("轻点计算时长（毫秒）").Text="100";
                Check(modernSettings.ArrowNavigationSpeed==4 && modernSettings.TapSeekSeconds==.4m &&
                    Descendants(inputCard).OfType<System.Windows.Controls.TextBlock>().Any(block=>block.Text.Contains("0.4 秒（4.00 × 100 毫秒")),
                    "editing rate and tap duration saves immediately and shows the converted 0.4 seconds in the settings page");
                var wheelPicker=Descendants(inputCard).OfType<System.Windows.Controls.ComboBox>().Single(box=>
                    System.Windows.Automation.AutomationProperties.GetName(box)=="普通滚轮");
                var ctrlWheelPicker=Descendants(inputCard).OfType<System.Windows.Controls.ComboBox>().Single(box=>
                    System.Windows.Automation.AutomationProperties.GetName(box)=="Ctrl＋滚轮");
                wheelPicker.SelectedItem=WheelActions.All.Single(action=>action.Id=="speed");
                ctrlWheelPicker.SelectedItem=WheelActions.All.Single(action=>action.Id=="volume");
                var mouseSettings=ModernSettings.Load(Path.Combine(config,"modern-ui.json"));
                Check(mouseSettings.WheelAction=="speed" && mouseSettings.CtrlWheelAction=="volume" && mouseSettings.CtrlMouseVideoView,
                    "both actual wheel dropdowns save independently without changing the Ctrl drag switch");
                wheelPicker.SelectedItem=WheelActions.All.Single(action=>action.Id=="volume");
                ctrlWheelPicker.SelectedItem=WheelActions.All.Single(action=>action.Id=="zoom");
                content.UpdateLayout(); preview.Clear(); preview.Render(content);
                encoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(preview));
                using(var previewFile=File.Create(Path.Combine(output,"navigation-settings.png"))) encoder.Save(previewFile);
                foreach(var section in sections) {
                    RenderSection(section,dpi);
                }
                preferences.SearchText="轻点计算时长"; await Task.Delay(80);
                Check(inputCard.Visibility==System.Windows.Visibility.Visible &&
                    Descendants(inputCard).OfType<System.Windows.Controls.TextBlock>().Any(block=>block.IsVisible && block.Text.Contains("0.4 秒（")),
                    "searching the tap-duration setting keeps its conversion and V21 threshold explanation visible");
                preferences.NavigateTo("快捷键与鼠标:");
                var tapRestore=Descendants(inputCard).OfType<System.Windows.Controls.Button>().Single(button=>
                    System.Windows.Automation.AutomationProperties.GetName(button)=="轻点计算时长（毫秒）恢复默认");
                tapRestore.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(modernSettings.TapCalculationMilliseconds==500,"tap restore uses the new 500ms factory value even with V21 migrated settings");
                var arrowRestore=Descendants(inputCard).OfType<System.Windows.Controls.Button>().Single(button=>
                    System.Windows.Automation.AutomationProperties.GetName(button)=="方向键快进 / 快退倍率（×）恢复默认");
                arrowRestore.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(modernSettings.ArrowNavigationSpeed==6 && modernSettings.TapSeekSeconds==3 &&
                    Descendants(inputCard).OfType<System.Windows.Controls.TextBlock>().Any(block=>block.Text.Contains("3 秒（6.00 × 500 毫秒")),
                    "arrow restore sets 6x and the conversion immediately shows a three-second tap");
                Editor("独立长按快捷键倍率（×）").Text="8.00";
                Call("SetModernShortcut",pause,"Ctrl+F9",false);
                modernSettings.MouseWheelVolume=false; modernSettings.VolumeBoost=true; modernSettings.ShowButtonLabels=true;
                modernSettings.Bookmarks.Add(new VideoBookmark(media,42,"reset audit")); Call("SaveModernSettings");
                var beforeReset=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(config,"modern-ui.json")))!.AsObject();
                beforeReset["Positions"]!["reset-audit-video.mp4"]=42.5;
                File.WriteAllText(Path.Combine(config,"modern-ui.json"),beforeReset.ToJsonString());
                App.Settings.RecentFiles.Add("reset-audit-video.mp4");
                File.AppendAllText(Path.Combine(config,"mpv.conf"),"\nbrightness=17\n");
                Player.SetPropertyDouble("brightness",17);
                var resetButton=(System.Windows.Controls.Button)preferences.FindName("RestoreAllDefaultsButton");
                resetButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Task.Delay(120);
                var reset=State("defaults-reset"); string backup=reset.GetProperty("backup").GetString()!;
                Check(reset.GetProperty("restored").GetBoolean() && Directory.Exists(backup) &&
                    File.ReadAllText(Path.Combine(backup,"mpv.conf")).Contains("brightness=17"),
                    "the real restore-all button backs up the original configuration before resetting it");
                var resetSettings=ModernSettings.Load(Path.Combine(config,"modern-ui.json"));
                Check(resetSettings.TemporaryFastForwardSpeed==3 && resetSettings.ArrowNavigationSpeed==6 &&
                    resetSettings.TapCalculationMilliseconds==500 && resetSettings.TapSeekSeconds==3m &&
                    resetSettings.MouseWheelVolume && resetSettings.WheelAction=="volume" && resetSettings.CtrlWheelAction=="zoom" && !resetSettings.VolumeBoost && !resetSettings.ShowButtonLabels &&
                    resetSettings.ShortcutOverrides.Count==0 && resetSettings.MouseActions.Count==0 && resetSettings.Toolbar.Count==6,
                    "global defaults include all new navigation, mouse, volume and toolbar preferences");
                Check(resetSettings.Positions.GetValueOrDefault("reset-audit-video.mp4")==42.5 &&
                    resetSettings.Bookmarks.Any(mark=>mark.Title=="reset audit") && App.Settings.RecentFiles.Contains("reset-audit-video.mp4"),
                    "restore-all preserves persisted playback positions, bookmarks and recent files");
                Check(!File.ReadAllText(Path.Combine(config,"mpv.conf")).Contains("brightness=17") &&
                    Player.GetPropertyDouble("brightness")==0 && Player.GetPropertyDouble("speed")==1 &&
                    Field<ModernSettings>("_modernSettings").ShortcutOverrides.Count==0,
                    "advanced config, live picture adjustment and normal speed return to defaults without stale settings controls");
                bool resetPause=Player.Paused; Player.CommandV("keypress","Space");
                await Until(()=>Player.Paused!=resetPause,"default Space works after global reset");
                Check(true,"reset reinstates native default input bindings instead of leaving Space disabled");
                Player.CommandV("keypress","ESC"); await Task.Delay(80);
                Check(!host.IsDisposed && host.Visible,"restore-all defaults does not bring back Escape-to-quit");
                Player.SetPropertyBool("pause",true);
                Player.CommandV("keypress","Ctrl+F9"); await Task.Delay(60);
                Check(Player.Paused,"global reset removes an old customized shortcut from the live player");
                // Edit a native value, evict its view by visiting other pages,
                // then return. Cache eviction must never lose unsaved edits.
                var nativeModels=(List<Setting>)typeof(ConfWindow).GetField("_settings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preferences)!;
                var template=nativeModels.Single(setting=>setting.Name=="screenshot-template");
                preferences.NavigateTo(template.Directory+":"); await Task.Delay(60);
                var nativeViews=(Dictionary<Setting,System.Windows.FrameworkElement>)typeof(ConfWindow).GetField("_settingControls",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preferences)!;
                var templateView=(StringSettingControl)nativeViews[template]; templateView.Text="audit-cache";
                foreach(var category in preferences.FilterStrings.ToArray()) preferences.NavigateTo(category+":");
                Check(nativeViews.Count<=32 && !nativeViews.ContainsKey(template) && template.Value=="audit-cache",
                    "native controls cache is bounded and evicting a hidden view preserves the edited model value");
                preferences.NavigateTo(template.Directory+":"); await Task.Delay(40);
                Check(((StringSettingControl)nativeViews[template]).Text=="audit-cache","a recreated native control shows its unsaved edited value");
                preferences.SearchText="";
                SettingsEscape();
                Check(!preferences.IsVisible && !host.IsDisposed,"Escape closes an empty-search settings window while playback stays open");
                Check(nativeViews.Count==0 && panel.Children.Count==0,"closing settings releases cached and custom settings views");
                Check(File.ReadAllText(Path.Combine(config,"mpv.conf")).Contains("screenshot-template=audit-cache"),"the evicted native setting is saved when the settings window closes");
            } catch(Exception e) {
                failure=e; Console.Error.WriteLine(e);
                Console.Error.WriteLine(JsonSerializer.Serialize(new { time=Player.GetPropertyDouble("time-pos"),seeking=Player.GetPropertyBool("seeking"),
                    path=Player.GetPropertyString("path"),held=Field<int>("_heldDirection"),start=Field<string>("_requestedStart"),
                    resume=Field<string>("_resumeSource"),nativeStart=Player.GetPropertyString("file-local-options/start"),
                    feedback=State("navigation"), modifiers=Control.ModifierKeys.ToString(),
                    focused=Field<VideoSurface>("_videoSurface").Focused }));
            }
            finally { host.Close(); }
        };
        Application.Run(host);
        File.WriteAllText(Path.Combine(output,"observations.json"),JsonSerializer.Serialize(new {checks,samples,keyEvents,failure=failure?.ToString()},new JsonSerializerOptions {WriteIndented=true}));
        return failure==null && (previewOnly || checks.Count>20) ? 0 : 1;
    }
}
