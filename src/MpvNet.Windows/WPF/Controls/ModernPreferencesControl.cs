using System.Windows;
using System.Windows.Controls;
using MpvNet.Windows.Modern;
using MpvNet.Windows.WinForms;

namespace MpvNet.Windows.WPF;

public sealed partial class ModernPreferencesControl : UserControl, ISettingControl
{
    public Setting Setting { get; }
    public List<Setting> SearchableSettings { get; } = new();
    readonly Dictionary<Setting, FrameworkElement> _rows = new();
    readonly List<FrameworkElement> _help = new();
    readonly List<Border> _sections = new();
    TextBlock _heading = null!;
    readonly string _directory, _title;

    public void ShowOnly(Setting? setting)
    {
        foreach (var row in _rows) row.Value.Visibility =
            setting == null || row.Key == setting || row.Key.Name == "modern.navigation-conversion" &&
                setting.Name is "modern.navigation-arrow" or "modern.navigation-tap" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in _help) item.Visibility = setting == null ? Visibility.Visible : Visibility.Collapsed;
        foreach (var section in _sections) section.Visibility = _rows.Values.Any(row => row.Visibility == Visibility.Visible && row.IsDescendantOf(section))
            ? Visibility.Visible : Visibility.Collapsed;
        _heading.Text = setting == null ? _title : setting.DisplayTitle;
    }

    void Register(string name, string title, string help, FrameworkElement row)
    {
        var setting = new StringSetting { Name=name, DisplayTitle=title, Directory=_directory, Help=help };
        SearchableSettings.Add(setting); _rows.Add(setting,row);
    }
    public bool Contains(string search) => SearchableSettings.Any(setting => SettingSearch.Matches(setting,search));

    void Section(StackPanel parent,string title,string name,out StackPanel stack)
    {
        stack=new StackPanel();
        var border=new Border { Name=name, Background=ModernSettingsTheme.Current.MenuBackground,
            BorderBrush=ModernSettingsTheme.Current.MenuHighlight, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(8),
            Padding=new Thickness(20), Margin=new Thickness(0,0,0,14), Child=stack };
        System.Windows.Automation.AutomationProperties.SetName(border,title);
        var heading=new TextBlock { Text=title, FontSize=18, FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,0,0,16), Foreground=ModernSettingsTheme.Current.Foreground };
        _heading ??= heading; stack.Children.Add(heading); _sections.Add(border); parent.Children.Add(border);
    }

    public ModernPreferencesControl(MainForm main,bool inputPage=false)
    {
        _directory=inputPage?"快捷键与鼠标":"常规";
        _title=inputPage?"快进倍率":"播放与显示";
        Setting=new StringSetting { Directory=_directory,Name=_title,DisplayTitle=_title };
        if(inputPage) { BuildInputSettings(main); ShowOnly(null); return; }
        var parent=new StackPanel(); Content=parent; Section(parent,_title,"PlaybackSection",out var stack);
        var settings=main.ModernPreferences;
        void Checkbox(string name,string title,string help,bool value,Action<bool> save) {
            var check=new CheckBox {Content=title,IsChecked=value,Foreground=ModernSettingsTheme.Current.Foreground,Margin=new Thickness(0,0,0,14),FontSize=13};
            check.Checked+=(_,_)=>{save(true);main.SaveModernPreferences();}; check.Unchecked+=(_,_)=>{save(false);main.SaveModernPreferences();};
            stack.Children.Add(check); Register(name,title,help,check);
        }
        Checkbox("modern.remember-position","记住每个视频的播放位置","断点 续播 进度 记忆 继续播放",settings.RememberPosition,value=>settings.RememberPosition=value);
        Checkbox("modern.remember-speed","退出后记住播放速度","倍速 倍率 记忆 快放 慢放",settings.RememberSpeed,value=>settings.RememberSpeed=value);
        Checkbox("modern.folder-playlist","自动列出当前文件夹的视频","同目录 播放列表 选集 文件夹 顺序播放",settings.AutoFolderPlaylist,value=>{settings.AutoFolderPlaylist=value;if(value)main.RefreshCurrentFolderPlaylist();});
        Checkbox("modern.hide-cursor","播放时自动隐藏光标（远程控制建议关闭）","鼠标 指针 光标",settings.AutoHideCursor,value=>settings.AutoHideCursor=value);
        Checkbox("modern.fullscreen-controls","全屏时自动收起控制条（离开后 2.5 秒）","全屏 工具栏 自动隐藏 底栏",settings.AutoHideFullscreenControls,value=>settings.AutoHideFullscreenControls=value);
        Checkbox("modern.volume-boost","声音增强（可调至 300%，开启时保留当前音量）","声音 音量 放大 增强 扩展 100 300 百分比",settings.VolumeBoost,value=>settings.VolumeBoost=value);
        Section(parent,"画面调节","PictureSection",out var pictureSection);
        var picture = new ModernPictureControl(main);
        pictureSection.Children.Add(picture);
        Register("modern.picture","视频亮度与夜间护眼","亮度 明暗 夜间 护眼 暖色 强度 色温 画面调节 恢复默认",picture);
        ShowOnly(null);
    }
}
