// Operates only this test process's own MainForm, controls and public mpv API.
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using MpvNet;
using MpvNet.MVVM;
using MpvNet.Windows;
using MpvNet.Windows.Modern;
using MpvNet.Windows.UI;
using MpvNet.Windows.WinForms;
using MpvNet.Windows.WPF;
using static MpvNet.Global;
using Wpf = System.Windows.Controls;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string root = Environment.GetEnvironmentVariable("MPVNET_TEST_ROOT")!;
        string media = Environment.GetEnvironmentVariable("MPVNET_TEST_MEDIA")!;
        string output = Path.Combine(root, "test-results/picture-v27");
        bool reopen = args.Length > 0;
        string config = reopen ? args[0] : Path.Combine(output, "画面测试-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(config);
        Environment.SetEnvironmentVariable("MPVNET_HOME", config);
        if (!reopen) {
            File.WriteAllText(Path.Combine(config, "mpvnet.conf"), "process-instance=multi\nauto-load-folder=no\nstart-size=always\n");
            File.WriteAllText(Path.Combine(config, "mpv.conf"), "osc=no\nborder=no\ntitle-bar=no\nkeepaspect-window=no\nkeep-open=yes\nvo=gpu\nhwdec=auto-safe\nao=null\nbrightness=17\n");
            File.WriteAllText(Path.Combine(config, "modern-ui.json"), JsonSerializer.Serialize(new { Width=1100, Height=760,
                WindowDpi=168, AutoFolderPlaylist=false, AutoHideCursor=false, RememberPosition=false }));
        }
        nint library = NativeLibrary.Load(Path.Combine(root, "dist/FrameFlow/libmpv-2.dll"));
        nint mediaInfo = NativeLibrary.Load(Path.Combine(root, "dist/FrameFlow/MediaInfo.dll"));
        NativeLibrary.SetDllImportResolver(typeof(MpvNet.Native.LibMpv).Assembly, (name, _, _) => name switch {
            "libmpv-2.dll" => library, "MediaInfo.dll" => mediaInfo, _ => nint.Zero
        });
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Translator.Current = new WpfTranslator(); App.Init(); Theme.Init();
        InstanceRouting.Initialize(Environment.ProcessPath!, App.ConfPath);
        using var host = new MainForm();
        object? Call(string method, params object[] values) => typeof(MainForm).GetMethod(method, BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(host, values);
        T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host)!;
        ModernSettings Settings() => Field<ModernSettings>("_modernSettings");
        void Brightness(int value) => Call("SetModernBrightness", value);
        void Night(bool value) => Call("SetModernNightMode", value);
        void Strength(int value) => Call("SetModernNightStrength", value);
        void Flush() => Call("FlushModernPictureChanges");
        var checks = new List<string>(); var samples = new List<object>(); Exception? failure = null;
        void Check(bool condition, string title) {
            if (!condition) throw new Exception(title);
            checks.Add(title); Console.WriteLine("PASS: " + title);
        }
        async Task Until(Func<bool> condition, string title) {
            var until = DateTime.UtcNow.AddSeconds(12);
            while (!condition()) { if (DateTime.UtcNow > until) throw new Exception("Timed out: " + title); await Task.Delay(25); }
        }
        IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject item) {
            yield return item;
            for (int i=0; i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(item); i++)
                foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(item, i))) yield return child;
        }
        void Render(System.Windows.FrameworkElement item, string file) {
            var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(item);
            var offset=System.Windows.Media.VisualTreeHelper.GetOffset(item);
            var visual=new System.Windows.Media.DrawingVisual();
            using (var drawing=visual.RenderOpen()) drawing.DrawRectangle(new System.Windows.Media.VisualBrush(item) {
                ViewboxUnits=System.Windows.Media.BrushMappingMode.Absolute,
                Viewbox=new System.Windows.Rect(offset.X,offset.Y,item.ActualWidth,item.ActualHeight), Stretch=System.Windows.Media.Stretch.Fill
            },null,new System.Windows.Rect(0,0,item.ActualWidth,item.ActualHeight));
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(item.ActualWidth*dpi.DpiScaleX),
                (int)Math.Ceiling(item.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var png=new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(output,file)); png.Save(stream);
        }
        host.Shown += async (_, _) => {
            try {
                Player.SetPropertyBool("pause",true);
                Player.CommandV("loadfile",media);
                await Until(()=>!Player.VideoSize.IsEmpty && Field<string>("_currentFile").Length>0,"video loaded");
                await Task.Delay(250);
                if (reopen) {
                    Check(Player.GetPropertyInt("brightness")==23 && Settings().VideoBrightness==23,
                        "a fresh application process restores saved video brightness before playback");
                    Check(Settings().NightMode && Settings().NightStrength==65 && Player.GetPropertyStringList("glsl-shaders").Length==1,
                        "a fresh application process restores exactly one night shader at the saved strength");
                    using var frame=ScreenshotImage.CaptureBitmap(Player,"window"); frame.Save(Path.Combine(output,"reopened.png"));
                    return;
                }
                Check(Player.GetPropertyInt("brightness")==17 && Settings().VideoBrightness==17,
                    "upgrading adopts legacy mpv.conf brightness rather than silently resetting it");
                string custom=Path.Combine(config,"原有着色器.glsl");
                File.WriteAllText(custom,"//!HOOK OUTPUT\n//!BIND HOOKED\nvec4 hook() { return HOOKED_tex(HOOKED_pos); }\n");
                Player.CommandV("change-list","glsl-shaders","append",custom);
                Check(Player.GetPropertyStringList("glsl-shaders").SequenceEqual(new[]{custom}),
                    "native string-list reader preserves an existing shader path containing Chinese characters");
                Brightness(0); await Task.Delay(150);
                using var original=ScreenshotImage.CaptureBitmap(Player,"window"); original.Save(Path.Combine(output,"original.png"));
                var whites=new List<Point>();
                for (int y=original.Height/4;y<original.Height*3/4;y+=4) for(int x=0;x<original.Width;x+=4) {
                    var pixel=original.GetPixel(x,y); if(pixel.R>242 && pixel.G>242 && pixel.B>242) whites.Add(new Point(x,y));
                }
                Check(whites.Count>100,"reference video exposes a white sample area for actual GPU pixel verification");
                (double R,double G,double B) Mean(Bitmap image) => (whites.Average(p=>(double)image.GetPixel(p.X,p.Y).R),
                    whites.Average(p=>(double)image.GetPixel(p.X,p.Y).G), whites.Average(p=>(double)image.GetPixel(p.X,p.Y).B));
                Brightness(-30); await Task.Delay(150);
                using(var dark=ScreenshotImage.CaptureBitmap(Player,"window")) {
                    var mean=Mean(dark); Check(mean.R<Mean(original).R-30,"brightness slider changes the displayed paused video pixels immediately");
                    dark.Save(Path.Combine(output,"brightness-minus-30.png")); samples.Add(new {kind="dark",mean.R,mean.G,mean.B});
                }
                Brightness(0); Night(true); await Task.Delay(150);
                using(var warm=ScreenshotImage.CaptureBitmap(Player,"window")) {
                    var mean=Mean(warm); Check(mean.R>mean.G+5 && mean.G>mean.B+15,"default 35% night mode actually reduces blue relative to red and green in the GPU output");
                    Check(Player.GetPropertyInt("brightness")==0,"night mode leaves the independent brightness adjustment unchanged");
                    warm.Save(Path.Combine(output,"night-35.png")); samples.Add(new {kind="night35",mean.R,mean.G,mean.B});
                }
                Strength(100); Flush(); await Task.Delay(150);
                using(var strong=ScreenshotImage.CaptureBitmap(Player,"window")) {
                    var mean=Mean(strong); Check(mean.B<150 && mean.G<220 && mean.R>240,"100% warmth strengthens the tint without changing red-channel brightness");
                    Check(strong.GetPixel(2,strong.Height/2).ToArgb()==Color.Black.ToArgb(),"video letterbox remains pure black under the night shader");
                    strong.Save(Path.Combine(output,"night-100.png")); samples.Add(new {kind="night100",mean.R,mean.G,mean.B});
                }
                Night(false); Flush(); await Task.Delay(150);
                using(var restored=ScreenshotImage.CaptureBitmap(Player,"window")) {
                    var mean=Mean(restored); var before=Mean(original);
                    Check(Math.Abs(mean.R-before.R)<2 && Math.Abs(mean.G-before.G)<2 && Math.Abs(mean.B-before.B)<2,
                        "turning night mode off restores the original GPU pixel colors");
                }
                Check(Player.GetPropertyStringList("glsl-shaders").SequenceEqual(new[]{custom}),"turning night mode off preserves unrelated user shaders");
                Settings().TryAddToolbarItem(ToolbarCatalog.Find("night-mode")!.Item); Call("RebuildToolbar");
                var nightButton=Field<List<(ToolbarItem Item,ModernButton Button)>>("_toolButtons").Single(pair=>pair.Item.Id=="night-mode").Button;
                Player.CommandV("script-message-to","mpvnet","modern-tool","night-mode");
                await Until(()=>Settings().NightMode,"night tool message");
                Check(nightButton.Checked && Player.GetPropertyStringList("glsl-shaders").Length==2,
                    "the common tool action toggles night mode and updates its editable-toolbar selection state");
                Check(Call("SetModernShortcut",ShortcutCatalog.Find("night-mode")!,"Ctrl+F10",false)==null,
                    "the new night action accepts a user-selected keyboard shortcut");
                Player.CommandV("keypress","Ctrl+F10"); await Until(()=>!Settings().NightMode,"assigned night shortcut");
                Check(!nightButton.Checked && Player.GetPropertyStringList("glsl-shaders").SequenceEqual(new[]{custom}),
                    "the assigned shortcut switches night off, clears the toolbar selection and preserves other shaders");
                Call("SetModernShortcut",ShortcutCatalog.Find("night-mode")!,"",true);
                Night(true);
                for(int i=0;i<100;i++) Strength((i%20+1)*5);
                Flush();
                Check(Player.GetPropertyStringList("glsl-shaders").Length==2 && Settings().NightStrength==100,
                    "rapid strength edits coalesce to the latest target without accumulating active shaders");
                foreach(int value in Enumerable.Range(1,20).Select(i=>i*5)) { Strength(value); Flush(); await Task.Delay(5); }
                Check(Directory.GetFiles(Path.Combine(config,"shaders/modern-night-v1")).Length<=20 &&
                    Player.GetPropertyStringList("glsl-shaders").Length==2,"all strength levels use a bounded set of files and exactly one active night shader");
                var preferences=new ConfWindow(); preferences.Show(); await Task.Delay(120);
                var panel=(Wpf.StackPanel)preferences.FindName("MainStackPanel");
                var card=panel.Children.OfType<ModernPreferencesControl>().Single(control=>control.Setting.Directory=="常规");
                var picture=Descendants(card).OfType<ModernPictureControl>().Single();
                var brightness=Descendants(picture).OfType<Wpf.Slider>().Single(slider=>slider.Name=="BrightnessSlider");
                var strength=Descendants(picture).OfType<Wpf.Slider>().Single(slider=>slider.Name=="NightStrengthSlider");
                var toggle=Descendants(picture).OfType<Wpf.CheckBox>().Single();
                brightness.Value=28; strength.Value=60; Flush(); await Task.Delay(80);
                Check(Player.GetPropertyInt("brightness")==28 && Settings().NightStrength==60,"the actual settings sliders apply and save independent brightness and warmth");
                var section=Descendants(card).OfType<Wpf.Border>().Single(border=>border.Name=="PictureSection");
                section.BringIntoView(); await Task.Delay(80); Render(section,"settings-picture.png");
                preferences.SearchText="夜间护眼"; await Task.Delay(50);
                Check(card.Visibility==System.Windows.Visibility.Visible && picture.Visibility==System.Windows.Visibility.Visible,
                    "global settings search can find and edit the new picture controls");
                var resetBrightness=Descendants(picture).OfType<Wpf.Button>().Single(button=>
                    System.Windows.Automation.AutomationProperties.GetName(button)=="视频亮度恢复默认");
                resetBrightness.RaiseEvent(new System.Windows.RoutedEventArgs(Wpf.Button.ClickEvent));
                Check(Player.GetPropertyInt("brightness")==0 && toggle.IsChecked==true,"brightness restore defaults does not turn off night mode");
                toggle.IsChecked=false; Flush();
                Check(!strength.IsEnabled && Player.GetPropertyStringList("glsl-shaders").SequenceEqual(new[]{custom}),
                    "night off disables the strength slider and removes only its own effect");
                toggle.IsChecked=true;
                var resetStrength=Descendants(picture).OfType<Wpf.Button>().Single(button=>
                    System.Windows.Automation.AutomationProperties.GetName(button)=="夜间护眼强度恢复默认");
                resetStrength.RaiseEvent(new System.Windows.RoutedEventArgs(Wpf.Button.ClickEvent)); Flush();
                Check(Settings().NightStrength==35 && Settings().NightMode,"warmth restore defaults returns to 35% without changing the switch");
                preferences.Close();
                await Task.Delay(80);
                int Listeners()=>((Delegate?)typeof(MainForm).GetField("ModernPictureChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(host))?.GetInvocationList().Length??0;
                int listeners=Listeners();
                Call("ShowPicture"); await Task.Delay(120);
                var popup=Field<ToolStripDropDown>("_picturePopup");
                var content=(System.Windows.Forms.Integration.ElementHost)((ToolStripControlHost)popup.Items[0]).Control;
                Render((System.Windows.FrameworkElement)content.Child,"compact-picture.png");
                Call("ShowPicture"); await Task.Delay(60);
                Console.WriteLine($"Popup released: fieldNull={Field<ToolStripDropDown?>("_picturePopup")==null}, disposed={popup.IsDisposed}, listeners before={listeners} after={Listeners()}");
                Check(Field<ToolStripDropDown?>("_picturePopup")==null && popup.IsDisposed && Listeners()==listeners,
                    "clicking the picture button again closes the compact panel and releases its view and listener");
                Brightness(45); Strength(90); Night(true); Flush();
                Call("RestoreAllPlayerDefaults"); await Task.Delay(120);
                Check(Player.GetPropertyInt("brightness")==0 && !Settings().NightMode && Settings().NightStrength==35 &&
                    Player.GetPropertyStringList("glsl-shaders").SequenceEqual(new[]{custom}),
                    "restore all resets both new preferences live while preserving an unrelated shader");
                var reset=ModernSettings.Load(Path.Combine(config,"modern-ui.json"));
                Check(reset.VideoBrightness==0 && !reset.NightMode && reset.NightStrength==35,"restore-all picture defaults survive reopening the settings file");
                Brightness(23); Night(true); Strength(65); Flush();
                Player.CommandV("loadfile",media);
                await Until(()=>Player.VideoSize.Width>0 && Player.GetPropertyStringList("glsl-shaders").Length==2,"reload video");
                Check(Player.GetPropertyInt("brightness")==23 && Settings().NightStrength==65,"switching videos retains the chosen picture profile without duplicating its shader");
                Player.SetPropertyInt("brightness",-12); await Task.Delay(100); Flush();
                Check(Settings().VideoBrightness==-12,"native brightness shortcuts also synchronize the new saved picture control");
                Brightness(23); Flush();
            }
            catch(Exception ex) { failure=ex; Console.Error.WriteLine(ex); }
            finally { host.Close(); }
        };
        Application.Run(host);
        File.WriteAllText(Path.Combine(output,reopen?"observations-reopen.json":"observations.json"),
            JsonSerializer.Serialize(new {config,checks,samples,failure=failure?.ToString()},new JsonSerializerOptions{WriteIndented=true}));
        return failure==null ? 0 : 1;
    }
}
