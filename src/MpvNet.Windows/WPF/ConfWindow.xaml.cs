
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.Input;

using MpvNet.Help;
using MpvNet.Windows.UI;
using MpvNet.Windows.WPF.Controls;
using MpvNet.Windows.WPF.ViewModels;
using MpvNet.Windows.Modern;
using MpvNet.Windows.WinForms;

namespace MpvNet.Windows.WPF;

public partial class ConfWindow : Window, INotifyPropertyChanged
{
    public static string VersionText => ModernVersion.Label;
    readonly long _openedAt = Stopwatch.GetTimestamp();
    List<Setting> _settings = Conf.LoadConf(Properties.Resources.editor_conf.TrimEnd());
    readonly Dictionary<Setting, FrameworkElement> _settingControls = new();
    readonly LinkedList<Setting> _nativeControlOrder = new();
    const int NativeControlCacheLimit = 32;
    int _createdNativeControls;
    readonly Dictionary<Setting, int> _settingOrder = new();
    ModernPreferencesControl? _preferencesControl;
    ModernPreferencesControl? _inputPreferencesControl;
    ShortcutSettingsControl? _shortcutControl;
    bool _settingsReady;
    List<ConfItem> _confItems = new List<ConfItem>();
    string _initialContent;
    string _themeConf = GetThemeConf();
    string? _searchText;
    List<NodeViewModel>? _nodes;
    bool _shown;
    double _firstRenderMilliseconds;
    string _selectedCategory = "常规";
    readonly List<Setting> _searchResults = new();
    int _searchResultIndex;
    bool _selectingNode;
    int _useSpace;
    int _useNoSpace;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConfWindow(string? initialSearch = null)
    {
        InitializeComponent();
        ModernSettingsTheme.Apply(this);
        DataContext = this;
        ChineseSettings.Apply(_settings);
        SearchControl.SearchTextBox.Height = 36;
        SearchControl.SearchTextBox.FontSize = 12;
        SearchControl.SearchTextBox.Padding = new Thickness(7, 6, 24, 6);
        SearchControl.SearchTextBox.PreviewKeyDown += Search_KeyDown;
        if (MainForm.Instance != null) {
            _preferencesControl = new ModernPreferencesControl(MainForm.Instance) { Visibility = Visibility.Collapsed };
            MainStackPanel.Children.Add(_preferencesControl);
            _shortcutControl = new ShortcutSettingsControl(MainForm.Instance) { Visibility = Visibility.Collapsed };
            _inputPreferencesControl = new ModernPreferencesControl(MainForm.Instance, true) { Visibility = Visibility.Collapsed };
            MainStackPanel.Children.Add(_inputPreferencesControl);
            MainStackPanel.Children.Add(_shortcutControl);
            FilterStrings.Add("快捷键与鼠标");
        }
        LoadConf(Player.ConfPath);
        LoadConf(App.ConfPath);
        LoadLibplaceboConf();
        LoadSettings();
        _initialContent = GetCompareString();
        _settingsReady = true;

        // A normal open always starts in General with all branches collapsed.
        NavigateTo(initialSearch ?? "常规:");
    }

    public ObservableCollection<string> FilterStrings { get; } = new();

    public Theme? Theme => ModernSettingsTheme.Current;

    public string SearchText
    {
        get => _searchText ?? "";
        set
        {
            _searchText = value ?? "";
            SearchTextChanged();
            OnPropertyChanged();
        }
    }

    public void NavigateTo(string target)
    {
        target = ChineseSettings.Search(target);
        if (target.EndsWith(':') && FilterStrings.Contains(target[..^1])) {
            _selectedCategory = target[..^1];
            SearchText = "";
            SelectCategoryNode(_selectedCategory);
        } else SearchText = target;
    }

    public void ResetNavigation()
    {
        foreach (var node in Nodes) CollapseNode(node);
        NavigateTo("常规:");
    }

    static void CollapseNode(NodeViewModel node)
    {
        foreach (var child in node.Children) CollapseNode(child);
        node.IsExpanded = false;
    }

    public List<NodeViewModel> Nodes
    {
        get
        {
            if (_nodes == null)
            {
                var rootNode = new TreeNode();

                foreach (Setting setting in _settings)
                    AddNode(rootNode.Children, setting.Directory!);
                AddNode(rootNode.Children, "快捷键与鼠标");

                _nodes = new NodeViewModel(rootNode).Children;
            }

            return _nodes;
        }
    }

    public static TreeNode? AddNode(IList<TreeNode> nodes, string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (int x = 0; x < parts.Length; x++)
        {
            bool found = false;
 
            foreach (var node in nodes)
            {
                if (x < parts.Length - 1)
                {
                    if (node.Name == parts[x])
                    {
                        found = true;
                        nodes = node.Children;
                    }
                }
                else if (x == parts.Length - 1 && node.Name == parts[x])
                {
                    found = true;
                }
            }

            if (!found)
            {
                if (x == parts.Length - 1)
                {
                    var item = new TreeNode() { Name = parts[x] };
                    nodes?.Add(item);
                    return item;
                }
            }
        }

        return null;
    }

    void LoadSettings()
    {
        foreach (Setting setting in _settings)
        {
            setting.StartValue = setting.Value;

            if (!FilterStrings.Contains(setting.Directory!))
                FilterStrings.Add(setting.Directory!);

            foreach (ConfItem item in _confItems)
            {
                if (setting.Name == item.Name &&
                    setting.File == item.File &&
                    item.Section == "" && !item.IsSectionItem)
                {
                    setting.Value = item.Value;
                    setting.StartValue = setting.Value;
                    setting.ConfItem = item;
                    item.SettingBase = setting;
                }
            }

            _settingOrder.Add(setting, _settingOrder.Count);
        }
    }

    FrameworkElement GetSettingControl(Setting setting)
    {
        if (_settingControls.TryGetValue(setting, out var existing)) {
            _nativeControlOrder.Remove(setting); _nativeControlOrder.AddLast(setting); return existing;
        }
        FrameworkElement control = setting switch {
            StringSetting text => new StringSettingControl(text),
            OptionSetting option when option.Options.Count > 3 => new ComboBoxSettingControl(option),
            OptionSetting option => new OptionSettingControl(option),
            _ => throw new InvalidOperationException("不支持的设置类型")
        };
        control.Visibility = Visibility.Collapsed;
        int index = (_preferencesControl == null ? 0 : 3) + _settingControls.Keys.Count(item => _settingOrder[item] < _settingOrder[setting]);
        MainStackPanel.Children.Insert(index, control);
        _settingControls.Add(setting, control);
        _nativeControlOrder.AddLast(setting); _createdNativeControls++;
        return control;
    }

    void TrimNativeControlCache()
    {
        var node=_nativeControlOrder.First;
        while(_settingControls.Count>NativeControlCacheLimit && node!=null) {
            var next=node.Next; var control=_settingControls[node.Value];
            // Active controls may exceed the limit in a large category. Only
            // hidden views are discarded; the settings models retain edits.
            if(control.Visibility!=Visibility.Visible) {
                MainStackPanel.Children.Remove(control); control.DataContext=null;
                _settingControls.Remove(node.Value); _nativeControlOrder.Remove(node);
            }
            node=next;
        }
    }

    void ReleaseSettingsViews()
    {
        foreach(var control in _settingControls.Values) control.DataContext=null;
        MainStackPanel.Children.Clear(); _settingControls.Clear(); _nativeControlOrder.Clear();
        _preferencesControl=null; _inputPreferencesControl=null; _shortcutControl=null;
        DataContext=null;
    }

    void PublishSettingsState(bool visible = true)
    {
        if (Player.Handle == IntPtr.Zero) return;
        Player.SetPropertyString("user-data/modern/settings", JsonSerializer.Serialize(new {
            visible, ready = _shown && visible, search = SearchText,
            createdControls = _settingControls.Count, totalSettings = _settings.Count,
            nativeControlCacheLimit = NativeControlCacheLimit, createdNativeControls = _createdNativeControls,
            openMilliseconds = _firstRenderMilliseconds, category = _selectedCategory,
            results = new { count = _searchResults.Count, index = _searchResults.Count == 0 ? 0 : _searchResultIndex + 1,
                current = _searchResults.ElementAtOrDefault(_searchResultIndex)?.Name,
                matches = _searchResults.Select(setting => new { name = setting.Name, title = setting.DisplayTitle, category = setting.Directory }) },
            categories = Nodes.Select(node => new { name = node.Name, expanded = node.IsExpanded, selected = node.IsSelected }),
            visibleSettings = _settings.Where(setting => _settingControls.TryGetValue(setting, out var control) && control.Visibility == Visibility.Visible)
                .Select(setting => new { name = setting.Name, title = setting.DisplayTitle, value = setting.Value })
        }));
    }

    static string GetThemeConf() => Theme.DarkMode + App.DarkTheme + App.LightTheme;

    string GetCompareString() => string.Join("", _settings.Select(item => item.Name + item.Value).ToArray());

    void LoadConf(string file)
    {
        if (!File.Exists(file))
            return;

        // Resource schemas keep their historical identifier; the public file
        // name changed. Use the same logical kind for values and comments.
        string configKind = Path.GetFileNameWithoutExtension(file);
        if (configKind.Equals("frameflow", StringComparison.OrdinalIgnoreCase)) configKind = "mpvnet";

        string comment = "";
        string section = "";

        bool isSectionItem = false;

        foreach (string it in File.ReadAllLines(file))
        {
            string line = it.Trim();

            if (line.StartsWith("-"))
                line = line.TrimStart('-');

            if (line == "")
                comment += "\r\n";
            else if (line.StartsWith("#"))
                comment += line.Trim() + "\r\n";
            else if (line.StartsWith("[") && line.Contains(']'))
            {
                if (!isSectionItem && comment != "" && comment != "\r\n")
                    _confItems.Add(new ConfItem() {
                        Comment = comment, File = configKind});

                section = line.Substring(0, line.IndexOf("]") + 1);
                comment = "";
                isSectionItem = true;
            }
            else if (line.Contains('=') || Regex.Match(line, "^[\\w-]+$").Success)
            {
                if (!line.Contains('='))
                {
                    if (line.StartsWith("no-"))
                    {
                        line = line.Substring(3);
                        line += "=no";
                    }
                    else
                        line += "=yes";
                }

                if (line.Contains(" =") || line.Contains("= "))
                    _useSpace += 1;
                else
                    _useNoSpace += 1;

                ConfItem item = new();
                item.File = configKind;
                item.IsSectionItem = isSectionItem;
                item.Comment = comment;
                comment = "";
                item.Section = section;
                section = "";

                if (line.Contains('#') && !line.Contains('\'') && !line.Contains('"'))
                {
                    item.LineComment = line.Substring(line.IndexOf("#")).Trim();
                    line = line.Substring(0, line.IndexOf("#")).Trim();
                }

                int pos = line.IndexOf("=");
                string left = line.Substring(0, pos).Trim().ToLower().TrimStart('-');
                string right = line.Substring(pos + 1).Trim();
                
                if (right.StartsWith('\'') && right.EndsWith('\''))
                    right = right.Trim('\'');

                if (right.StartsWith('"') && right.EndsWith('"'))
                    right = right.Trim('"');

                if (left == "fs")
                    left = "fullscreen";

                if (left == "loop")
                    left = "loop-file";

                item.Name = left;
                item.Value = right;
                _confItems.Add(item);
            }
        }
        // Retain comments after the last option, including those at the end of
        // a named profile. They must survive edits in any settings category.
        if (comment.Length > 0)
            _confItems.Add(new ConfItem { File = configKind, Comment = comment, IsSectionItem = isSectionItem });
    }

    string GetKeyValueContent(string filename)
    {
        List<string> pairs = new();

        foreach (Setting setting in _settings)
        {
            if (filename != setting.File)
                continue;

            if ((setting.Value ?? "") != setting.Default)
                pairs.Add(setting.Name + "=" + EscapeValue(setting.Value!));
        }

        return string.Join(',', pairs);
    }

    void LoadLibplaceboConf()
    {
        foreach (ConfItem item in _confItems.ToArray())
            if (item.Name == "libplacebo-opts")
                LoadKeyValueList(item.Value, "libplacebo");
    }

    void LoadKeyValueList(string options, string file)
    {
        string[] optionStrings = options.Split(",", StringSplitOptions.RemoveEmptyEntries);

        foreach (string pair in optionStrings)
        {
            if (!pair.Contains('='))
                continue;

            int pos = pair.IndexOf("=");
            string left = pair.Substring(0, pos).Trim().ToLower();
            string right = pair.Substring(pos + 1).Trim();

            ConfItem item = new();
            item.Name = left;
            item.Value = right;
            item.File = file;
            _confItems.Add(item);
        }
    }

    string EscapeValue(string value)
    {
        if (value.Contains('\''))
            return '"' + value + '"';

        if (value.Contains('"'))
            return '\'' + value + '\'';

        if (value.Contains('"') || value.Contains('#') || value.StartsWith("%") ||
            value.StartsWith(" ") || value.EndsWith(" "))
        {
            return '\'' + value + '\'';
        }

        return value;
    }

    string GetContent(string filename)
    {
        StringBuilder sb = new StringBuilder();
        List<string> namesWritten = new List<string>();
        string equalString = _useSpace > _useNoSpace ? " = " : "=";

        foreach (ConfItem item in _confItems)
        {
            if (filename != item.File || item.Section != "" || item.IsSectionItem)
                continue;

            if (item.Comment != "")
                sb.Append(item.Comment);

            if (item.SettingBase == null)
            {
                if (item.Name != "")
                {
                    sb.Append(item.Name + equalString + EscapeValue(item.Value));

                    if (item.LineComment != "")
                        sb.Append(" " + item.LineComment);

                    sb.AppendLine();
                    namesWritten.Add(item.Name);
                }
            }
            else if ((item.SettingBase.Value ?? "") != item.SettingBase.Default)
            {
                sb.Append(item.Name + equalString + EscapeValue(item.SettingBase.Value!));

                if (item.LineComment != "")
                    sb.Append(" " + item.LineComment);

                sb.AppendLine();
                namesWritten.Add(item.Name);
            }
        }

        foreach (Setting setting in _settings)
        {
            if (filename != setting.File || namesWritten.Contains(setting.Name!))
                continue;

            if ((setting.Value ?? "") != setting.Default)
                sb.AppendLine(setting.Name + equalString + EscapeValue(setting.Value!));
        }

        foreach (ConfItem item in _confItems)
        {
            if (filename != item.File || (item.Section == "" && !item.IsSectionItem))
                continue;

            if (item.Section != "")
            {
                if (!sb.ToString().EndsWith("\r\n\r\n"))
                    sb.AppendLine();

                sb.AppendLine(item.Section);
            }

            if (item.Comment != "")
                sb.Append(item.Comment);

            if (item.Name == "") continue;
            sb.Append(item.Name + equalString + EscapeValue(item.Value));

            if (item.LineComment != "")
                sb.Append(" " + item.LineComment);

            sb.AppendLine();
            namesWritten.Add(item.Name);
        }

        return "\r\n" + sb.ToString().Trim() + "\r\n";
    }

    void SearchTextChanged()
    {
        if (!_settingsReady) return;
        _searchResults.Clear();
        _searchResultIndex = 0;
        string query = SearchText.Trim();
        if (query.Length > 0) {
            var candidates = (_preferencesControl?.SearchableSettings ?? []).Concat(_inputPreferencesControl?.SearchableSettings ?? [])
                .Concat(_shortcutControl?.SearchableSettings ?? []).Concat(_settings);
            _searchResults.AddRange(candidates.Where(setting => SettingSearch.Matches(setting, query))
                .OrderByDescending(setting => SettingSearch.Rank(setting, query)));
        }
        ShowSearchResult();
    }

    void ShowSearchResult()
    {
        if (_inputPreferencesControl != null) _inputPreferencesControl.Visibility = Visibility.Collapsed;
        if (_shortcutControl != null) _shortcutControl.Visibility = Visibility.Collapsed;
        foreach (var control in _settingControls.Values) control.Visibility = Visibility.Collapsed;
        if (_preferencesControl != null) {
            _preferencesControl.Visibility = Visibility.Collapsed;
        }
        bool searching = SearchText.Trim().Length > 0;
        SearchResultsBar.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        SearchEmptyMessage.Visibility = searching && _searchResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PreviousResultButton.IsEnabled = NextResultButton.IsEnabled = _searchResults.Count > 1;
        SearchResultCount.Text = _searchResults.Count == 0 ? "0 个匹配项" : $"第 {_searchResultIndex + 1} / {_searchResults.Count} 个匹配项";
        SearchResultLocation.Text = "搜索范围：全部设置";
        if (searching) {
            if (_searchResults.Count > 0) {
                var setting = _searchResults[_searchResultIndex];
                _selectedCategory = setting.Directory ?? "常规";
                SearchResultLocation.Text = _selectedCategory.Replace("/", " › ") + " › " + setting.DisplayTitle;
                if (_preferencesControl?.SearchableSettings.Contains(setting) == true) {
                    _preferencesControl.ShowOnly(setting);
                    _preferencesControl.Visibility = Visibility.Visible;
                } else if (_inputPreferencesControl?.SearchableSettings.Contains(setting) == true) {
                    _inputPreferencesControl.ShowOnly(setting); _inputPreferencesControl.Visibility = Visibility.Visible;
                } else if (_shortcutControl?.SearchableSettings.Contains(setting) == true) {
                    _shortcutControl.ShowOnly(setting); _shortcutControl.Visibility = Visibility.Visible;
                } else GetSettingControl(setting).Visibility = Visibility.Visible;
                SelectCategoryNode(_selectedCategory);
            }
        } else {
            if (_preferencesControl != null && _selectedCategory == "常规") { _preferencesControl.ShowOnly(null); _preferencesControl.Visibility = Visibility.Visible; }
            if (_selectedCategory == "快捷键与鼠标") {
                if (_shortcutControl != null) { _shortcutControl.ShowOnly(null); _shortcutControl.Visibility = Visibility.Visible; }
                if (_inputPreferencesControl != null) { _inputPreferencesControl.ShowOnly(null); _inputPreferencesControl.Visibility = Visibility.Visible; }
            }
            foreach (var setting in _settings.Where(setting => setting.Directory == _selectedCategory))
                GetSettingControl(setting).Visibility = Visibility.Visible;
        }
        TrimNativeControlCache();
        MainScrollViewer.ScrollToTop();
        if (_shown) PublishSettingsState();
    }

    void MoveSearchResult(int delta)
    {
        if (_searchResults.Count < 2) return;
        _searchResultIndex = (_searchResultIndex + delta + _searchResults.Count) % _searchResults.Count;
        ShowSearchResult();
    }

    void PreviousResult_Click(object sender, RoutedEventArgs e) => MoveSearchResult(-1);
    void NextResult_Click(object sender, RoutedEventArgs e) => MoveSearchResult(1);

    void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.F3 && SearchText.Trim().Length > 0) {
            MoveSearchResult((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
            e.Handled = true;
        }
    }
    
    void ConfWindow1_Loaded(object sender, RoutedEventArgs e)
    {
        SearchControl.SearchTextBox.SelectAll();
        Keyboard.Focus(SearchControl.SearchTextBox);

        foreach (var i in MainStackPanel.Children.OfType<StringSettingControl>())
            i.Update();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try {
        PublishSettingsState(false);
        App.Settings.ConfigEditorSearch = SearchText;

        if (_initialContent == GetCompareString())
            return;

        foreach (Setting setting in _settings)
        {
            if (setting.Name == "libplacebo-opts")
            {
                setting.Value = GetKeyValueContent("libplacebo");
                break;
            }
        }

        File.WriteAllText(Player.ConfPath, GetContent("mpv"));
        File.WriteAllText(App.ConfPath, GetContent("mpvnet"));

        foreach (Setting it in _settings)
        {
            if (it.Value != it.StartValue)
            {
                if (it.File == "mpv")
                {
                    Player.ProcessProperty(it.Name, it.Value);
                    Player.SetPropertyString(it.Name!, it.Value!);
                }
                else if (it.File == "mpvnet")
                    App.ProcessProperty(it.Name ?? "", it.Value ?? "", true);
            }
        }

        Theme.Init();
        Theme.UpdateWpfColors();

        if (_themeConf != GetThemeConf())
            MessageBox.Show("主题选项已保存，重新启动播放器后生效。", "设置已保存");
        } finally { ReleaseSettingsViews(); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        
        if (e.Key == Key.Escape) {
            if (SearchText.Length > 0) SearchText = "";
            else Close();
            e.Handled = true;
        }
        if (e.Key == Key.F3 && SearchText.Trim().Length > 0) {
            MoveSearchResult((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
            e.Handled = true;
        } else if (e.Key == Key.F3 || (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control))
        {
            Keyboard.Focus(SearchControl.SearchTextBox);
            SearchControl.SearchTextBox.SelectAll();
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_shown)
            return;

        _shown = true;
        _firstRenderMilliseconds = Math.Round(Stopwatch.GetElapsedTime(_openedAt).TotalMilliseconds, 1);
        PublishSettingsState();

        Application.Current.Dispatcher.BeginInvoke(() => {
            SearchControl.SearchTextBox.SelectAll();
        },
        DispatcherPriority.Background);
    }

    void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!_selectingNode && TreeView.SelectedItem is NodeViewModel node && _settingsReady && node.Path != _selectedCategory)
            NavigateTo(node.Path + ":");
    }
    void Done_Click(object sender, RoutedEventArgs e) => Close();

    void RestoreAllDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (MainForm.Instance == null) return;
        try {
            string backup = MainForm.Instance.RestoreAllPlayerDefaults();
            MainStackPanel.Children.Clear(); _settingControls.Clear(); _nativeControlOrder.Clear(); _settingOrder.Clear(); _confItems.Clear(); FilterStrings.Clear();
            _settings = Conf.LoadConf(Properties.Resources.editor_conf.TrimEnd()); ChineseSettings.Apply(_settings);
            _preferencesControl = new ModernPreferencesControl(MainForm.Instance) { Visibility = Visibility.Collapsed };
            _shortcutControl = new ShortcutSettingsControl(MainForm.Instance) { Visibility = Visibility.Collapsed };
            _inputPreferencesControl = new ModernPreferencesControl(MainForm.Instance, true) { Visibility = Visibility.Collapsed };
            MainStackPanel.Children.Add(_preferencesControl); MainStackPanel.Children.Add(_inputPreferencesControl); MainStackPanel.Children.Add(_shortcutControl);
            LoadConf(Player.ConfPath); LoadConf(App.ConfPath); LoadLibplaceboConf(); LoadSettings();
            FilterStrings.Add("快捷键与鼠标"); _nodes = null; OnPropertyChanged(nameof(Nodes));
            _initialContent = GetCompareString(); _themeConf = GetThemeConf(); NavigateTo("常规:");
            DefaultsStatus.Text = "已恢复全部设置；断点和书签保留。底层参数重启后生效。";
            DefaultsStatus.ToolTip = "原配置备份：" + backup;
        } catch (Exception error) { DefaultsStatus.Text = "恢复未完成：" + error.Message; }
    }

    void TreeView_CategoryClick(object sender, MouseButtonEventArgs e)
    {
        // Clicking the already highlighted category also exits search. The
        // expand/collapse arrow remains independent from category navigation.
        if (e.OriginalSource is TextBlock { DataContext: NodeViewModel node }) NavigateTo(node.Path + ":");
    }

    void SelectCategoryNode(string category)
    {
        _selectingNode = true;
        try {
            foreach (var node in Nodes) {
                UnselectNode(node);
                Select(node);
            }
        } finally { _selectingNode = false; }

        void Select(NodeViewModel node)
        {
            if (node.Path == category) {
                if (node.Parent != null) node.Parent.IsExpanded = true;
                node.IsSelected = true;
            }
            foreach (var child in node.Children) Select(child);
        }
    }

    void UnselectNode(NodeViewModel node)
    {
        if (node.IsSelected)
            node.IsSelected = false;

        foreach (var it in node.Children)
            UnselectNode(it);
    }

    [RelayCommand] void ShowMpvNetSpecificSettings() => SearchText = "FrameFlow";

    [RelayCommand] void PreviewMpvConfFile() => Msg.ShowInfo(GetContent("mpv"));
    
    [RelayCommand] void PreviewMpvNetConfFile() => Msg.ShowInfo(GetContent("mpvnet"));

    [RelayCommand] void ShowMpvManual() => ProcessHelp.ShellExecute("https://mpv.io/manual/master/");
    
    [RelayCommand] void ShowMpvNetManual() => ProcessHelp.ShellExecute(Path.Combine(Folder.Startup, "使用说明.md"));
}
