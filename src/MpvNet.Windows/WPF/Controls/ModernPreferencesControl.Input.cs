using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MpvNet.Windows.Modern;
using MpvNet.Windows.WinForms;

namespace MpvNet.Windows.WPF;

public sealed partial class ModernPreferencesControl
{
    void BuildInputSettings(MainForm main)
    {
        var settings=main.ModernPreferences; var parent=new StackPanel(); Content=parent;
        Section(parent,"快进倍率","NavigationSection",out var navigation);
        var result=new TextBlock {Foreground=ModernSettingsTheme.Current.Foreground2,TextWrapping=TextWrapping.Wrap};
        void UpdateResult()=>result.Text=$"轻点一次前进 / 后退 {settings.TapSeekSeconds:0.####} 秒（{settings.ArrowNavigationSpeed:0.00} × {settings.TapCalculationMilliseconds:0.##} 毫秒 ÷ 1000）。\n触发判断沿用 V21：约 180 毫秒内松开为轻点；持续按住才进入长按。计算时长不改变触发门槛。";
        void Number(string name,string title,Func<decimal> read,Action<decimal> save,Func<decimal> reset,decimal minimum,decimal maximum,string format) {
            var row=new Grid {Margin=new Thickness(0,0,0,12)};
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(104)});
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(102)});
            row.Children.Add(new TextBlock {Text=title,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Foreground=ModernSettingsTheme.Current.Foreground});
            var box=new TextBox {Text=read().ToString(format,CultureInfo.InvariantCulture),Height=32,VerticalContentAlignment=VerticalAlignment.Center,
                Background=ModernSettingsTheme.Current.Background,Foreground=ModernSettingsTheme.Current.Foreground,Margin=new Thickness(4,0,8,0)};
            System.Windows.Automation.AutomationProperties.SetName(box,title);
            box.TextChanged+=(_,_)=> {
                if(decimal.TryParse(box.Text,NumberStyles.Number,CultureInfo.InvariantCulture,out var value)&&value>=minimum&&value<=maximum) {
                    box.BorderBrush=ModernSettingsTheme.Current.MenuHighlight; save(Math.Round(value,2)); main.SaveModernPreferences(); UpdateResult();
                } else box.BorderBrush=Brushes.IndianRed;
            };
            box.LostKeyboardFocus+=(_,_)=>box.Text=read().ToString(format,CultureInfo.InvariantCulture);
            var restore=new Button {Content="恢复默认",Height=32,Padding=new Thickness(6,0,6,0)};
            System.Windows.Automation.AutomationProperties.SetName(restore,title+"恢复默认");
            restore.Click+=(_,_)=>{save(reset());box.Text=read().ToString(format,CultureInfo.InvariantCulture);main.SaveModernPreferences();UpdateResult();};
            Grid.SetColumn(box,1); Grid.SetColumn(restore,2); row.Children.Add(box); row.Children.Add(restore); navigation.Children.Add(row);
            Register(name,title,"快进 快退 倍率 长按 轻点 毫秒 计算 恢复默认 独立 与播放速度分开",row);
        }
        Number("modern.navigation-temporary","独立长按快捷键倍率（×）",()=>settings.TemporaryFastForwardSpeed,value=>settings.TemporaryFastForwardSpeed=value,()=>ModernSettings.DefaultTemporarySpeed,1,16,"0.00");
        Number("modern.navigation-arrow","方向键快进 / 快退倍率（×）",()=>settings.ArrowNavigationSpeed,value=>settings.ArrowNavigationSpeed=value,()=>ModernSettings.DefaultArrowSpeed,1,16,"0.00");
        Number("modern.navigation-tap","轻点计算时长（毫秒）",()=>settings.TapCalculationMilliseconds,value=>settings.TapCalculationMilliseconds=value,()=>ModernSettings.DefaultTapMilliseconds,1,1000,"0.##");
        navigation.Children.Add(result); UpdateResult(); Register("modern.navigation-conversion","轻点换算结果与触发门槛","轻点 毫秒 秒数 换算 180 阈值 长按 判断",result);
        Section(parent,"鼠标操作","MouseSection",out var mouse);
        void Picker(StackPanel section,string name,string title,DoubleClickAction[] actions,string selected,Action<string> save,string help) {
            var row=new Grid {Margin=new Thickness(0,0,0,10)};
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(168)});
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            row.Children.Add(new TextBlock {Text=title,VerticalAlignment=VerticalAlignment.Center,Foreground=ModernSettingsTheme.Current.Foreground});
            var picker=new ComboBox {ItemsSource=actions,DisplayMemberPath=nameof(DoubleClickAction.Title),SelectedItem=actions.First(action=>action.Id==selected),MinHeight=34,
                Foreground=ModernSettingsTheme.Current.Foreground,Background=ModernSettingsTheme.Current.Background};
            System.Windows.Automation.AutomationProperties.SetName(picker,title);
            picker.SelectionChanged+=(_,_)=>{if(picker.SelectedItem is DoubleClickAction action){save(action.Id);main.SaveModernPreferences();}};
            Grid.SetColumn(picker,1);row.Children.Add(picker);section.Children.Add(row);Register(name,title,help+" "+string.Join(' ',actions.Select(action=>action.Title)),row);
        }
        Picker(mouse,"modern.wheel-action","普通滚轮",WheelActions.All,settings.WheelAction,value=>{settings.WheelAction=value;settings.MouseWheelVolume=value=="volume";},"鼠标 滚轮 音量 倍率 缩放 自定义");
        Picker(mouse,"modern.ctrl-wheel-action","Ctrl＋滚轮",WheelActions.All,settings.CtrlWheelAction,value=>settings.CtrlWheelAction=value,"鼠标 滚轮 Ctrl 音量 倍率 缩放 自定义");
        Picker(mouse,"modern.mouse-view","Ctrl＋左键拖动",MouseDragActions.All,settings.CtrlDragAction,
            value=>{settings.CtrlDragAction=value;settings.CtrlMouseVideoView=value=="pan";},"鼠标 左键 平移 拖动 Ctrl 与滚轮动作独立");
        var clicks=new[]{new DoubleClickAction("input-conf","跟随快捷键配置"),new DoubleClickAction("ignore","不执行操作"),new DoubleClickAction("script-message-to mpvnet show-menu","右键菜单")}
            .Concat(ShortcutCatalog.All.Where(action=>action.Id is "pause" or "fullscreen" or "previous" or "next" or "mute" or "zoom-reset" or "frame-back" or "frame-forward")
                .Select(action=>new DoubleClickAction(action.Command,action.Title))).ToArray();
        foreach(var (key,title) in new[]{("Ctrl+MBTN_Right","Ctrl＋右键单击"),("MBTN_Left","左键单击"),("MBTN_Right","右键单击"),("MBTN_Mid","中键单击"),("MBTN_Back","侧键 1"),("MBTN_Forward","侧键 2")}) {
            string current=settings.MouseActions.GetValueOrDefault(key,"input-conf");
            var choices=clicks.Any(action=>action.Id==current)?clicks:clicks.Append(new DoubleClickAction(current,"原有自定义操作")).ToArray();
            Picker(mouse,"modern.mouse-action-"+key,title,choices,current,value=>main.SetModernMouseAction(key,value),"鼠标 按钮 单击 侧键 Ctrl 自定义");
        }
        Picker(mouse,"modern.video-double-click","视频画面双击",DoubleClickActions.Video,settings.VideoDoubleClickAction,value=>settings.VideoDoubleClickAction=value,"鼠标 视频 双击");
        Picker(mouse,"modern.title-double-click","标题栏双击",DoubleClickActions.TitleBar,settings.TitleBarDoubleClickAction,value=>settings.TitleBarDoubleClickAction=value,"鼠标 标题栏 双击");
        var mouseHelp=new TextBlock {Text="滚轮、拖动和鼠标按钮分别选择动作。“跟随快捷键配置”沿用 input.conf 的对应绑定；Ctrl＋右键也可设为画面复位等常用功能。",
            TextWrapping=TextWrapping.Wrap,Foreground=ModernSettingsTheme.Current.Foreground2,FontSize=12};
        mouse.Children.Add(mouseHelp);_help.Add(mouseHelp);
        Section(parent,"画面提示","FeedbackSection",out var feedback);
        Picker(feedback,"modern.navigation-feedback","左上角位置提示",new[]{new DoubleClickAction("on","显示时间与百分比（3 秒）"),new DoubleClickAction("off","不显示")},
            settings.ShowNavigationFeedback?"on":"off",value=>settings.ShowNavigationFeedback=value=="on","逐帧 跳转 定位 提示 反馈 OSD 上一帧 下一帧 三秒");
        var feedbackHelp=new TextBlock {Text="控制逐帧、快进、快退和定位后的提示显示，与鼠标执行什么动作分开。长按期间持续更新，松开后保留约 3 秒。",
            TextWrapping=TextWrapping.Wrap,Foreground=ModernSettingsTheme.Current.Foreground2,FontSize=12};
        feedback.Children.Add(feedbackHelp);_help.Add(feedbackHelp);
    }
}
