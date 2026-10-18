using System.Windows;
using System.Windows.Controls;
using MpvNet.Windows.Modern;
using MpvNet.Windows.WinForms;

namespace MpvNet.Windows.WPF;

public sealed class ModernPictureControl : UserControl
{
    readonly MainForm _main;
    readonly Slider _brightness, _strength;
    readonly TextBlock _brightnessValue, _strengthValue;
    readonly CheckBox _night;
    bool _updating, _attached;

    public ModernPictureControl(MainForm main)
    {
        _main = main;
        Foreground = ModernSettingsTheme.Current.Foreground;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Background = ModernSettingsTheme.Current.MenuBackground;
        var stack = new StackPanel { Margin = new Thickness(12) };
        Content = stack;
        Grid Caption(string title, out TextBlock value, string restoreTitle, Action restore)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            value = new TextBlock { TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 1); row.Children.Add(value);
            var reset = new Button { Content = "恢复默认", Height = 28, Padding = new Thickness(4, 0, 4, 0),
                Background = ModernSettingsTheme.Current.MenuHighlight, Foreground = ModernSettingsTheme.Current.Foreground,
                BorderBrush = ModernSettingsTheme.Current.Foreground2, Template = CreateResetTemplate() };
            System.Windows.Automation.AutomationProperties.SetName(reset, restoreTitle);
            reset.Click += (_, _) => restore();
            Grid.SetColumn(reset, 2); row.Children.Add(reset);
            stack.Children.Add(row);
            return row;
        }
        Slider Slider(string name, string title, int min, int max, int tick)
        {
            var slider = new Slider { Name = name, Minimum = min, Maximum = max, TickFrequency = tick,
                IsSnapToTickEnabled = true, SmallChange = tick, LargeChange = tick * 5, MinHeight = 28,
                IsMoveToPointEnabled = true, Template = CreateSliderTemplate(), Margin = new Thickness(0, 0, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(slider, title);
            slider.LostMouseCapture += (_, _) => _main.FlushModernPictureChanges();
            slider.LostKeyboardFocus += (_, _) => _main.FlushModernPictureChanges();
            stack.Children.Add(slider);
            return slider;
        }
        Caption("视频亮度", out _brightnessValue, "视频亮度恢复默认", () => main.SetModernBrightness(0));
        _brightness = Slider("BrightnessSlider", "视频亮度（-100 至 100，0 为原始亮度）", -100, 100, 1);
        _brightness.ValueChanged += (_, _) => { if (!_updating) main.SetModernBrightness((int)_brightness.Value); };
        _night = new CheckBox { Name = "NightModeToggle", Content = "夜间护眼（暖色画面）",
            Foreground = ModernSettingsTheme.Current.Foreground, Margin = new Thickness(0, 0, 0, 12) };
        _night.Checked += (_, _) => { if (!_updating) main.SetModernNightMode(true); };
        _night.Unchecked += (_, _) => { if (!_updating) main.SetModernNightMode(false); };
        stack.Children.Add(_night);
        Caption("暖色强度", out _strengthValue, "夜间护眼强度恢复默认", () => main.SetModernNightStrength(ModernSettings.DefaultNightStrength));
        _strength = Slider("NightStrengthSlider", "夜间护眼暖色强度（0 至 100%）", 0, 100, 5);
        _strength.ValueChanged += (_, _) => { if (!_updating) main.SetModernNightStrength((int)_strength.Value); };
        stack.Children.Add(new TextBlock { Text = "亮度 0 为原始画面；暖色强度可调，关闭后恢复原色。",
            TextWrapping = TextWrapping.Wrap, Foreground = ModernSettingsTheme.Current.Foreground2, FontSize = 11 });
        Loaded += (_, _) => {
            if (!_attached) { _main.ModernPictureChanged += Refresh; _attached = true; }
            Refresh();
        };
        Unloaded += (_, _) => Detach();
        Refresh();
    }

    static ControlTemplate CreateResetTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Border Name="Frame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="4">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="{TemplateBinding Padding}" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#0096FF" /></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Frame" Property="BorderBrush" Value="#0096FF" /></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Frame" Property="Opacity" Value="0.7" /></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    static ControlTemplate CreateSliderTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Slider">
          <Grid Height="26" Background="Transparent">
            <Track Name="PART_Track">
              <Track.DecreaseRepeatButton>
                <RepeatButton Command="Slider.DecreaseLarge" Focusable="False">
                  <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="3" Background="#0096FF" CornerRadius="1.5" /></ControlTemplate></RepeatButton.Template>
                </RepeatButton>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command="Slider.IncreaseLarge" Focusable="False">
                  <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="3" Background="#464E5C" CornerRadius="1.5" /></ControlTemplate></RepeatButton.Template>
                </RepeatButton>
              </Track.IncreaseRepeatButton>
              <Track.Thumb><Thumb Width="12" Height="12"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#6FA7FF" /></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            </Track>
          </Grid>
          <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter TargetName="PART_Track" Property="Opacity" Value="0.4" /></Trigger></ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    void Refresh()
    {
        _updating = true;
        try {
            var settings = _main.ModernPreferences;
            _brightness.Value = settings.VideoBrightness;
            _brightnessValue.Text = settings.VideoBrightness == 0 ? "0" : settings.VideoBrightness.ToString("+0;-0");
            _night.IsChecked = settings.NightMode;
            _strength.Value = settings.NightStrength;
            _strength.IsEnabled = settings.NightMode;
            _strengthValue.Text = settings.NightStrength + "%";
        }
        finally { _updating = false; }
    }

    internal void Detach()
    {
        if (_attached) { _main.ModernPictureChanged -= Refresh; _attached = false; }
        _main.FlushModernPictureChanges();
    }
}
