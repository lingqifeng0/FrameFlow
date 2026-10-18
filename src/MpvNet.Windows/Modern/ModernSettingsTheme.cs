using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MpvNet.Windows.Native;
using MpvNet.Windows.UI;

namespace MpvNet.Windows.Modern;

public static class ModernSettingsTheme
{
    public static Theme Current { get; } = Create();
    static Brush Brush(System.Drawing.Color color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
    static Theme Create() => new() {
        Background = Brush(ModernTheme.Background), Foreground = Brush(ModernTheme.Text),
        Foreground2 = Brush(ModernTheme.Muted), Heading = Brush(ModernTheme.Accent),
        MenuBackground = Brush(ModernTheme.Surface), MenuHighlight = Brush(ModernTheme.Hover)
    };
    public static void Apply(Window window)
    {
        if (ModernTheme.ApplicationIcon is { } icon) {
            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            window.Icon = image;
        }
        window.Resources["PrimaryTextBrush"] = Current.Foreground;
        window.Resources["SecondaryTextBrush"] = Current.Foreground2;
        window.Resources["RegionBrush"] = Current.Background;
        window.Resources["SecondaryRegionBrush"] = Current.MenuBackground;
        window.Resources["BorderBrush"] = Current.MenuHighlight;
        window.Resources["PrimaryBrush"] = Current.Heading;
        window.SourceInitialized += (_, _) => WinApi.DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, new[] { 1 }, 4);
    }
}
