using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using System.Threading.Tasks;
using MpvNet.Windows.Modern;
using MpvNet.Native;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    Panel _modernBody = null!;
    Panel _playlistPane = null!;
    Panel _playlistSplitter = null!;
    ListBox _playlistList = null!;
    Label _playlistTitle = null!;
    Label _playlistEmpty = null!;
    ModernButton _playlistRemove = null!;
    ModernButton _playlistModeButton = null!;
    string _folderPlaylistFile = "";
    string _playlistDirectory = "";
    readonly List<PlaylistEntry> _playlistEntries = [];
    int _playlistPlayingIndex = -1;
    bool _playlistResizing;
    int _playlistResizeStartX;
    int _playlistResizeStartWidth;
    string _playlistKeepOpen = "yes";
    string _playlistKeepOpenPause = "yes";
    bool _playlistOptionsCaptured;
    sealed record PlaylistEntry(string Path, string Title);

    void InitializePlaylistPane()
    {
        _modernBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        _playlistPane = new Panel { Dock = DockStyle.Right, Width = _modernSettings.PlaylistWidth, BackColor = ModernTheme.Background, Visible = false };
        _playlistSplitter = new Panel { Dock = DockStyle.Right, Width = 5, BackColor = ModernTheme.Surface, Cursor = Cursors.VSplit, Visible = false,
            AccessibleName = "调整播放列表宽度" };
        var heading = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(12, 0, 6, 0) };
        _playlistTitle = new Label { Text = "播放列表", Dock = DockStyle.Fill, ForeColor = ModernTheme.Text, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 10) };
        var close = MakeButton("收起播放列表", "\uE8BB", (_, _) => SetPlaylistVisible(false), 36); close.Dock = DockStyle.Right;
        heading.Controls.Add(_playlistTitle); heading.Controls.Add(close);
        var modes = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 0, 8, 4) };
        var refreshFolder = MakeButton("刷新当前目录视频", "\uE72C", (_, _) => RefreshCurrentFolderPlaylist(), 36);
        refreshFolder.Dock = DockStyle.Right;
        _playlistModeButton = MakeButton("播放模式", "", (_, _) => ShowPlaylistModes());
        _playlistModeButton.Text = "顺序播放 ▾"; _playlistModeButton.Dock = DockStyle.Fill;
        modes.Controls.Add(_playlistModeButton); modes.Controls.Add(refreshFolder);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 49, Padding = new Padding(8, 3, 0, 0), WrapContents = false };
        _playlistRemove = MakeButton("从列表移除选中项", "\uE74D", (_, _) => RemovePlaylistSelection());
        actions.Controls.AddRange([
            MakeButton("添加视频到列表", "\uE710", (_, _) => AddPlaylistFiles()),
            MakeButton("选中项上移", "\uE70E", (_, _) => MovePlaylistSelection(-1)),
            MakeButton("选中项下移", "\uE70D", (_, _) => MovePlaylistSelection(1)),
            _playlistRemove,
            MakeButton("保留当前视频，清除其他项", "\uE711", (_, _) => { Player.CommandV("playlist-clear"); RefreshPlaylistPane(); })
        ]);
        _playlistList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = ModernTheme.Background, ForeColor = ModernTheme.Text,
            Font = new Font("Microsoft YaHei UI", 9), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 38, IntegralHeight = false,
            AccessibleName = "右侧播放列表", AllowDrop = true };
        _playlistList.DrawItem += DrawPlaylistItem;
        _playlistList.DoubleClick += (_, _) => PlayPlaylistSelection();
        _playlistList.KeyDown += (_, e) => {
            if (e.KeyCode == Keys.Enter) { PlayPlaylistSelection(); e.Handled = e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Delete) { RemovePlaylistSelection(); e.Handled = e.SuppressKeyPress = true; }
        };
        _playlistList.MouseMove += (_, e) => {
            int index = _playlistList.IndexFromPoint(e.Location);
            string text = index >= 0 && index < _playlistEntries.Count ? _playlistEntries[index].Path : "";
            if (_modernTips.GetToolTip(_playlistList) != text) _modernTips.SetToolTip(_playlistList, text);
        };
        _playlistList.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        _playlistList.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) { Player.LoadFiles(files, false, true); RefreshPlaylistPane(); } };
        var context = new ContextMenuStrip { BackColor = ModernTheme.Surface, ForeColor = ModernTheme.Text };
        TrackFullscreenChromeMenu(context);
        context.Items.Add("播放选中项", null, (_, _) => PlayPlaylistSelection());
        context.Items.Add("上移", null, (_, _) => MovePlaylistSelection(-1));
        context.Items.Add("下移", null, (_, _) => MovePlaylistSelection(1));
        context.Items.Add("从列表移除", null, (_, _) => RemovePlaylistSelection());
        _playlistList.ContextMenuStrip = context;
        _playlistList.MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) _playlistList.SelectedIndex = _playlistList.IndexFromPoint(e.Location); };
        _playlistList.Disposed += (_, _) => context.Dispose();
        _playlistEmpty = new Label { Dock = DockStyle.Fill, Text = "列表为空\n点击下方 + 添加视频", TextAlign = ContentAlignment.MiddleCenter, ForeColor = ModernTheme.Muted, BackColor = ModernTheme.Background };
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(7, 0, 7, 0) };
        content.Controls.Add(_playlistList); content.Controls.Add(_playlistEmpty);
        _playlistPane.Controls.Add(content); _playlistPane.Controls.Add(actions); _playlistPane.Controls.Add(modes); _playlistPane.Controls.Add(heading);
        _modernBody.Controls.Add(_videoSurface); _modernBody.Controls.Add(_playlistSplitter); _modernBody.Controls.Add(_playlistPane);
        _modernBody.Resize += (_, _) => ConstrainPlaylistWidth();
        _playlistSplitter.MouseEnter += (_, _) => ShowCursor();
        _playlistSplitter.MouseDown += (_, e) => {
            if (e.Button != MouseButtons.Left) return;
            _playlistResizeStartX = _playlistSplitter.PointToScreen(e.Location).X;
            _playlistResizeStartWidth = _playlistPane.Width;
            _playlistResizing = true;
            _playlistSplitter.Capture = true;
        };
        _playlistSplitter.MouseMove += (_, e) => ResizePlaylist(e.Location);
        _playlistSplitter.MouseUp += (_, e) => {
            if (e.Button != MouseButtons.Left || !_playlistResizing) return;
            ResizePlaylist(e.Location);
            FinishPlaylistResize();
            _playlistSplitter.Capture = false;
        };
        _playlistSplitter.MouseCaptureChanged += (_, _) => { if (!_playlistSplitter.Capture) FinishPlaylistResize(); };
    }

    void ShowPlaylist() => SetPlaylistVisible(!_modernSettings.PlaylistVisible);
    void SetPlaylistVisible(bool visible)
    {
        _modernBody.SuspendLayout();
        _playlistPane.Visible = visible; _playlistSplitter.Visible = visible;
        _modernSettings.PlaylistVisible = visible;
        if (visible) _playlistPane.Width = (int)Math.Round(_modernSettings.PlaylistWidth * DeviceDpi / 96d);
        _modernBody.ResumeLayout(); ConstrainPlaylistWidth(); RefreshPlaylistPane(); SaveModernSettings();
    }
    void ConstrainPlaylistWidth()
    {
        if (!_playlistPane.Visible || _modernBody.Width <= 0 || _playlistResizing) return;
        var (minimum, maximum) = PlaylistWidthLimits();
        int preferred = (int)Math.Round(_modernSettings.PlaylistWidth * DeviceDpi / 96d);
        int width = Math.Clamp(preferred, minimum, maximum);
        if (_playlistPane.Width != width) _playlistPane.Width = width;
    }
    (int Minimum, int Maximum) PlaylistWidthLimits()
    {
        int maximum = Math.Max(1, Math.Min(600 * DeviceDpi / 96, Math.Min(_modernBody.ClientSize.Width * 2 / 5,
            _modernBody.ClientSize.Width - 260 * DeviceDpi / 96 - _playlistSplitter.Width)));
        return (Math.Min(210 * DeviceDpi / 96, maximum), maximum);
    }
    void ResizePlaylist(Point location)
    {
        if (!_playlistResizing) return;
        var (minimum, maximum) = PlaylistWidthLimits();
        int mouseX = _playlistSplitter.PointToScreen(location).X;
        int width = Math.Clamp(_playlistResizeStartWidth + _playlistResizeStartX - mouseX, minimum, maximum);
        if (_playlistPane.Width != width) _playlistPane.Width = width;
    }
    void FinishPlaylistResize()
    {
        if (!_playlistResizing) return;
        _playlistResizing = false;
        if (_modernClosing || _playlistPane.Width == _playlistResizeStartWidth) return;
        _modernSettings.PlaylistWidth = Math.Clamp((int)Math.Round(_playlistPane.Width * 96d / DeviceDpi), 210, 600);
        ConstrainPlaylistWidth();
        SaveModernSettings();
        RefreshPlaylistPane();
        PublishModernWindowState();
    }
    void RefreshPlaylistPane()
    {
        if (Player.Handle == IntPtr.Zero || _modernClosing) return;
        try {
            using var document = JsonDocument.Parse(Player.GetPropertyString("playlist"));
            var entries = document.RootElement.EnumerateArray().Select(entry => {
                string path = entry.TryGetProperty("filename", out var file) ? file.GetString() ?? "" : "";
                string title = entry.TryGetProperty("title", out var value) ? value.GetString() ?? "" : "";
                return new PlaylistEntry(path, string.IsNullOrWhiteSpace(title) ? Path.GetFileName(path) : title);
            }).ToList();
            int playing = Player.GetPropertyInt("playlist-pos");
            if (!_playlistEntries.SequenceEqual(entries)) {
                int selected = _playlistList.SelectedIndex, top = _playlistList.TopIndex;
                _playlistList.BeginUpdate();
                _playlistEntries.Clear(); _playlistEntries.AddRange(entries);
                _playlistList.Items.Clear();
                foreach (var entry in entries) _playlistList.Items.Add(entry.Title);
                _playlistList.SelectedIndex = selected >= 0 && selected < entries.Count ? selected : playing >= 0 && playing < entries.Count ? playing : -1;
                if (top >= 0 && top < entries.Count) _playlistList.TopIndex = top;
                _playlistList.EndUpdate();
            }
            if (_playlistPlayingIndex != playing) { _playlistPlayingIndex = playing; _playlistList.Invalidate(); }
            _playlistTitle.Text = $"播放列表 · {entries.Count}";
            _playlistEmpty.Visible = entries.Count == 0;
            _playlistRemove.Enabled = entries.Count > 0;
            string nativeMode = Player.GetPropertyString("loop-file") is not ("no" or "0" or "") ? "single-loop" :
                Player.GetPropertyString("loop-playlist") is not ("no" or "0" or "") ?
                    _modernSettings.PlaylistMode == "random" ? "random" : "list-loop" :
                Player.GetPropertyString("keep-open") == "always" ? "stop-current" : "sequence";
            _modernSettings.PlaylistMode = nativeMode;
            _playlistModeButton.Text = PlaylistModeTitle(nativeMode) + " ▾";
            _playlistModeButton.Invalidate();
            Player.SetPropertyString("user-data/modern/playlist", JsonSerializer.Serialize(new {
                visible = _modernSettings.PlaylistVisible, current = playing, count = entries.Count,
                width = (int)Math.Round(_playlistPane.Width * 96d / DeviceDpi), widthPixels = _playlistPane.Width,
                preferredWidth = _modernSettings.PlaylistWidth, directory = _playlistDirectory,
                autoFolder = _modernSettings.AutoFolderPlaylist, mode = nativeMode, modeTitle = PlaylistModeTitle(nativeMode),
                items = entries.Select(e => new { path = e.Path, title = e.Title })
            }));
        } catch (JsonException) { }
    }
    void DrawPlaylistItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _playlistEntries.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0, playing = e.Index == _playlistPlayingIndex;
        using var brush = new SolidBrush(selected ? ModernTheme.Hover : ModernTheme.Background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var rect = new Rectangle(e.Bounds.X + 9, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 18), e.Bounds.Height);
        string text = $"{(playing ? "▶" : (e.Index + 1).ToString("00"))}  {_playlistEntries[e.Index].Title}";
        TextRenderer.DrawText(e.Graphics, text, _playlistList.Font, rect, playing ? ModernTheme.Accent : ModernTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
    void PlayPlaylistSelection()
    {
        if (_playlistList.SelectedIndex < 0) return;
        SaveCurrentPosition(); SaveModernSettings(); Player.SetPropertyInt("playlist-pos", _playlistList.SelectedIndex); RefreshPlaylistPane();
    }
    void AddPlaylistFiles()
    {
        using var picker = new OpenFileDialog { Title = "添加视频到播放列表", Multiselect = true, Filter = "视频和音频|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv;*.m4v;*.ts;*.flv;*.mp3;*.flac;*.wav|所有文件|*.*" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        Player.LoadFiles(picker.FileNames, false, true); RefreshPlaylistPane();
    }
    void RemovePlaylistSelection()
    {
        if (_playlistList.SelectedIndex < 0) return;
        SaveCurrentPosition(); SaveModernSettings(); Player.CommandV("playlist-remove", _playlistList.SelectedIndex.ToString()); RefreshPlaylistPane();
    }
    void MovePlaylistSelection(int direction)
    {
        int from = _playlistList.SelectedIndex, to = from + direction;
        if (from < 0 || to < 0 || to >= _playlistEntries.Count) return;
        Player.CommandV("playlist-move", from.ToString(), to.ToString()); RefreshPlaylistPane();
        _playlistList.SelectedIndex = to;
    }

    static string PlaylistModeTitle(string mode) => mode switch {
        "single-loop" => "单个循环", "list-loop" => "列表循环", "random" => "随机播放", "stop-current" => "播完当前就停", _ => "顺序播放"
    };

    void ShowPlaylistModes()
    {
        var menu = new ContextMenuStrip { BackColor = ModernTheme.Surface, ForeColor = ModernTheme.Text };
        TrackFullscreenChromeMenu(menu);
        foreach (string mode in new[] { "sequence", "single-loop", "list-loop", "random", "stop-current" }) {
            var item = new ToolStripMenuItem(PlaylistModeTitle(mode)) { Checked = _modernSettings.PlaylistMode == mode };
            item.Click += (_, _) => ApplyPlaylistMode(mode);
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => BeginInvoke(() => menu.Dispose());
        menu.Show(_playlistModeButton, new Point(0, _playlistModeButton.Height));
    }

    void ApplyPlaylistMode(string mode, bool notify = true)
    {
        if (!_playlistOptionsCaptured) {
            // Return to the configured normal behavior when leaving stop-current.
            // "always" is owned by that mode, so another mode must clear it.
            _playlistKeepOpen = Player.GetPropertyString("keep-open") == "no" ? "no" : "yes";
            _playlistKeepOpenPause = Player.GetPropertyString("keep-open-pause");
            _playlistOptionsCaptured = true;
        }
        bool leavingRandom = _modernSettings.PlaylistMode == "random" && mode != "random";
        _modernSettings.PlaylistMode = mode;
        Player.SetPropertyString("keep-open", mode == "stop-current" ? "always" : _playlistKeepOpen);
        Player.SetPropertyString("keep-open-pause", mode == "stop-current" ? "yes" : _playlistKeepOpenPause);
        Player.SetPropertyString("loop-file", mode == "single-loop" ? "inf" : "no");
        Player.SetPropertyString("loop-playlist", mode is "list-loop" or "random" ? "inf" : "no");
        if (leavingRandom) Player.CommandV("playlist-unshuffle");
        if (mode == "random") Player.CommandV("playlist-shuffle");
        if (!notify) return;
        AllowNavigationInput();
        ShowOperationFeedback("playlist-mode", "播放模式：" + PlaylistModeTitle(mode));
        RefreshPlaylistPane(); SaveModernSettings();
    }

    public void RefreshCurrentFolderPlaylist()
    {
        if (!File.Exists(_currentFile)) { ShowToast("请先打开本地视频"); return; }
        _folderPlaylistFile = "";
        Player.CommandV("playlist-clear");
        EnsureCurrentFolderPlaylist(_currentFile, true);
    }

    void EnsureCurrentFolderPlaylist(string path, bool force = false)
    {
        if ((!_modernSettings.AutoFolderPlaylist && !force) || !File.Exists(path) || Player.GetPropertyInt("playlist-count") != 1) return;
        string fullPath = Path.GetFullPath(path);
        if (!FileTypes.IsVideo(Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant()) ||
            string.Equals(_folderPlaylistFile, fullPath, StringComparison.OrdinalIgnoreCase)) return;
        _folderPlaylistFile = fullPath;
        string directory = Path.GetDirectoryName(fullPath)!;
        var extensions = FileTypes.GetVideoExts().ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Folder enumeration stays off the UI thread. Only direct child videos
        // are included; explicitly assembled multi-file queues stay intact.
        Task.Run(() => {
            try {
                var files = Directory.EnumerateFiles(directory).Where(file => extensions.Contains(Path.GetExtension(file).TrimStart('.'))).ToList();
                files.Sort(new StringLogicalComparer());
                Ui(() => {
                    if (_folderPlaylistFile == fullPath) _folderPlaylistFile = "";
                    if (_modernClosing) return;
                    string current = Player.GetPropertyString("path");
                    if (_modernClosing || (!force && !_modernSettings.AutoFolderPlaylist) ||
                        !File.Exists(current) || !string.Equals(Path.GetFullPath(current), fullPath, StringComparison.OrdinalIgnoreCase) ||
                        Player.GetPropertyInt("playlist-count") != 1) return;
                    _playlistDirectory = directory;
                    int index = files.FindIndex(file => string.Equals(file, fullPath, StringComparison.OrdinalIgnoreCase));
                    foreach (var file in files.Where(file => !string.Equals(file, fullPath, StringComparison.OrdinalIgnoreCase)))
                        Player.CommandV("loadfile", file, "append");
                    if (index > 0) Player.CommandV("playlist-move", "0", (index+1).ToString());
                    if (_modernSettings.PlaylistMode == "random") Player.CommandV("playlist-shuffle");
                    RefreshPlaylistPane();
                });
            } catch (IOException) { Ui(() => ShowToast("无法读取当前视频的文件夹")); }
              catch (UnauthorizedAccessException) { Ui(() => ShowToast("无法读取当前视频的文件夹")); }
        });
    }
}
