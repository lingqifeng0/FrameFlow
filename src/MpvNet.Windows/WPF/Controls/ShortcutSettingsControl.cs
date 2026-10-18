using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MpvNet.Windows.Modern;
using MpvNet.Windows.WinForms;
using WpfBinding = System.Windows.Data.Binding;

namespace MpvNet.Windows.WPF.Controls;

public sealed class ShortcutSettingsControl : UserControl, ISettingControl
{
    public Setting Setting { get; } = new StringSetting { Name = "modern.shortcuts", Directory = "快捷键与鼠标", DisplayTitle = "常用快捷键" };
    public List<Setting> SearchableSettings { get; } = new();
    readonly MainForm _main;
    readonly ObservableCollection<Row> _rows = new();
    readonly DataGrid _grid;
    readonly TextBlock _message;
    readonly Dictionary<Setting, string> _searchActions = new();
    public bool Contains(string search) => SearchableSettings.Any(item => SettingSearch.Matches(item, search));
    public sealed record Row(ShortcutAction Action, string Key) {
        public string Title => Action.Title;
        public string Category => Action.Category;
        public string KeyText => Key.Length == 0 ? "未设置" : Key;
    }
    public void ShowOnly(Setting? setting)
    {
        _grid.Height = setting == null ? 360 : 100;
        _grid.MinHeight = setting == null ? 200 : 100;
        _rows.Clear();
        string? id = setting != null ? _searchActions.GetValueOrDefault(setting) : null;
        foreach (var action in ShortcutCatalog.All.Where(item => id == null || item.Id == id))
            _rows.Add(new(action, _main.ShortcutKey(action)));
    }
    public ShortcutSettingsControl(MainForm main)
    {
        _main = main;
        var theme = ModernSettingsTheme.Current;
        var stack = new StackPanel();
        Content = new Border { Background = theme.MenuBackground, BorderBrush = theme.MenuHighlight, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 14), Child = stack };
        stack.Children.Add(new TextBlock { Text = "常用快捷键", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = theme.Foreground });
        stack.Children.Add(new TextBlock { Text = "选中功能后录入按键。修改立即生效并保存；冲突时会提示。长按左右键可连续快进 / 快退。Esc 保留关闭菜单、调节面板和取消对话框的作用，默认不退出播放器。", TextWrapping = TextWrapping.Wrap,
            Foreground = theme.Foreground2, FontSize = 12, Margin = new Thickness(0, 10, 0, 14) });
        _grid = new DataGrid { ItemsSource = _rows, AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single,
            CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 200, Height = 360, RowHeight = 34, Background = theme.Background, Foreground = theme.Foreground,
            RowBackground = theme.Background, AlternatingRowBackground = theme.MenuBackground,
            BorderBrush = theme.MenuHighlight, GridLinesVisibility = DataGridGridLinesVisibility.None,
            CanUserReorderColumns = false, CanUserSortColumns = false };
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, theme.MenuBackground));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, theme.Foreground2));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, theme.MenuHighlight));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 8, 6, 8)));
        _grid.ColumnHeaderStyle = headerStyle;
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, theme.Foreground));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, theme.MenuHighlight));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, theme.Foreground));
        cellStyle.Triggers.Add(selected);
        _grid.CellStyle = cellStyle;
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0)));
        textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        var keyStyle = new Style(typeof(TextBlock), textStyle);
        keyStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new WpfBinding(nameof(Row.KeyText))));
        _grid.Columns.Add(new DataGridTextColumn { Header = "分类", Binding = new WpfBinding(nameof(Row.Category)), Width = 90, ElementStyle = textStyle });
        _grid.Columns.Add(new DataGridTextColumn { Header = "功能", Binding = new WpfBinding(nameof(Row.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = textStyle });
        _grid.Columns.Add(new DataGridTextColumn { Header = "快捷键", Binding = new WpfBinding(nameof(Row.KeyText)), Width = 180, ElementStyle = keyStyle });
        _grid.MouseDoubleClick += (_, _) => Edit();
        System.Windows.Automation.AutomationProperties.SetName(_grid, "常用快捷键列表");
        stack.Children.Add(_grid);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        void Button(string title, Action action) {
            var button = new Button { Content = title, MinWidth = 104, Height = 34,
                Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
            button.Click += (_, _) => action(); buttons.Children.Add(button);
        }
        Button("录入 / 修改", Edit);
        Button("清除绑定", () => Change(""));
        Button("恢复原绑定", () => Change("", true));
        stack.Children.Add(buttons);
        _message = new TextBlock { Foreground = theme.Foreground2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(_message);
        foreach (var action in ShortcutCatalog.All) {
            var setting = new StringSetting { Name = "shortcut." + action.Id, Directory = "快捷键与鼠标", DisplayTitle = action.Title + "快捷键",
                Help = action.Category + " 键盘 按键 快捷键 热键 录入 修改" };
            SearchableSettings.Add(setting); _searchActions[setting] = action.Id;
        }
        ShowOnly(null);
    }
    void Edit()
    {
        if (_grid.SelectedItem is not Row row) { _message.Text = "请先选中要设置的功能。"; return; }
        var binding = new MpvNet.Binding { Input = row.Key.Contains(" / ") ? "" : row.Key, Command = row.Action.Command };
        string previous = binding.Input;
        var window = new LearnWindow { InputItem = binding, Owner = Window.GetWindow(this) };
        ModernSettingsTheme.Apply(window);
        window.ShowDialog();
        if (binding.Input != previous) Change(binding.Input);
    }
    void Change(string key, bool restore = false)
    {
        if (_grid.SelectedItem is not Row row) { _message.Text = "请先选中要设置的功能。"; return; }
        string? error = _main.SetModernShortcut(row.Action, key, restore);
        _message.Text = error ?? "已保存并立即生效。";
        if (error != null) return;
        int index = _rows.IndexOf(row);
        _rows[index] = new(row.Action, _main.ShortcutKey(row.Action));
        _grid.SelectedIndex = index;
    }
}
