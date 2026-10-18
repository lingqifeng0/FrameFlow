using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;
using MpvNet.Windows.Modern;
using MpvNet.Windows.Native;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    VideoSurface _videoSurface = null!;
    Panel _modernHeader = null!;
    Panel _modernFooter = null!;
    Panel _modernHeaderContent = null!;
    Panel _modernFooterContent = null!;
    Panel _modernTools = null!;
    Label _modernTitle = null!;
    Label _modernTime = null!;
    Label _modernDuration = null!;
    Label _emptyMessage = null!;
    ModernButton _modernPlay = null!;
    ModernButton _modernSpeed = null!;
    ModernButton _modernVolume = null!;
    ModernButton _modernPin = null!;
    ModernSeek _modernSeek = null!;
    ModernSettings _modernSettings = null!;
    System.Windows.Forms.Timer _modernTimer = null!;
    readonly ToolTip _modernTips = new() { AutoPopDelay = 8000 };
    readonly List<(ToolbarItem Item, ModernButton Button)> _toolButtons = [];
    ModernButton _moreButton = null!;
    int _toolbarVisibleCount;
    bool _toolbarHasOverflow;
    int _toolbarLayoutPasses;
    string _modernSettingsPath = "";
    string _currentFile = "";
    double _currentPosition;
    readonly object _resumePositionLock = new();
    int _saveTick;
    bool _modernClosing;
    object _stateSnapshot = new { };
    ToolStripDropDown? _speedPopup;
    Label? _toast;
    DateTime _toastExpires;
    Control? _titleDragSource;
    Point _titleDragStart;

    void InitializeModernUi()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        _modernSettingsPath = Path.Combine(Player.ConfigFolder, "modern-ui.json");
        _modernSettings = ModernSettings.Load(_modernSettingsPath);
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Black;
        MinimumSize = new Size(520, 340);
        Size = new Size(960, 640); // Final physical size is restored after DPI scaling, in OnLoad.
        App.StartSize = "always";
        App.Settings.WindowSize = ClientSize;
        KeyPreview = true;

        _modernHeader = new RoundedPanel { Dock = DockStyle.Top, Height = 44, BackColor = ModernTheme.HeaderBackground };
        _modernHeaderContent = new BufferedPanel { Dock = DockStyle.Fill, BackColor = ModernTheme.HeaderBackground };
        var headerTools = new BufferedFlowLayoutPanel { Dock = DockStyle.Right, Width = 362, WrapContents = false, Padding = new Padding(4, 4, 24, 0), BackColor = ModernTheme.HeaderBackground };
        _modernTitle = new Label { Dock = DockStyle.Fill, Text = "", ForeColor = ModernTheme.HeaderForeground, Font = new Font("Microsoft YaHei UI", 10), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(17, 0, 0, 0), AutoEllipsis = true };
        void AlignHeaderContent()
        {
            // The panel's new safe inset replaces part of the old whitespace,
            // so it does not add a second gap or move the window buttons.
            int inset = _modernHeader.Padding.Right;
            var titlePadding = new Padding(Math.Max(0, 17 - _modernHeader.Padding.Left), 0, 0, 0);
            var toolsPadding = new Padding(4, 4, Math.Max(0, 24 - inset), 0);
            if (_modernTitle.Padding != titlePadding) _modernTitle.Padding = titlePadding;
            if (headerTools.Padding != toolsPadding) headerTools.Padding = toolsPadding;
            int width = 362 - inset;
            if (headerTools.Width != width) headerTools.Width = width;
        }
        _modernHeader.Layout += (_, _) => AlignHeaderContent();
        AlignHeaderContent();
        foreach (var titleControl in new Control[] { _modernTitle, _modernHeader, _modernHeaderContent }) {
            titleControl.MouseDown += DragTitle;
            titleControl.MouseMove += MoveTitle;
            titleControl.MouseUp += (_, _) => CancelTitleDrag();
            titleControl.MouseCaptureChanged += (_, _) => {
                if (!titleControl.Capture && _titleDragSource == titleControl) _titleDragSource = null;
            };
        }
        _modernPin = MakeButton("窗口置顶（Pin）", "\uE718", (_, _) => ExecuteTool(new("ontop", "窗口置顶")), 38);
        var windowDivider = new BufferedPanel { Width = 16, Height = 36, Margin = Padding.Empty, BackColor = ModernTheme.HeaderBackground };
        windowDivider.Paint += (_, e) => {
            using var pen = new Pen(Color.FromArgb(96, 190, 255));
            e.Graphics.DrawLine(pen, windowDivider.Width / 2, 8, windowDivider.Width / 2, windowDivider.Height - 8);
        };
        headerTools.Controls.AddRange([
            MakeButton("视频信息", "\uE946", (_, _) => ShowMediaInfo(), 38),
            MakeButton("打开视频", "\uE8E5", (_, _) => OpenVideo(), 38),
            MakeButton("播放列表", "\uE8FD", (_, _) => ShowPlaylist(), 38),
            _modernPin,
            MakeButton("设置", "\uE713", (_, _) => ShowModernSettings(), 38),
            windowDivider,
            MakeButton("最小化", "\uE921", (_, _) => WindowState = FormWindowState.Minimized, 32),
            MakeButton("最大化 / 还原", "\uE922", (_, _) => ToggleMaximize(), 32),
            MakeButton("关闭", "\uE8BB", (_, _) => Close(), 32)
        ]);
        foreach (ModernButton button in headerTools.Controls.OfType<ModernButton>()) {
            button.BackColor = ModernTheme.HeaderBackground;
            button.ForeColor = ModernTheme.HeaderForeground;
            button.HoverColor = ModernTheme.HeaderHover;
            button.ActiveColor = ModernTheme.HeaderAccent;
            button.IconSize = button.Width == 32 ? 18 : 24;
        }
        _modernHeaderContent.Controls.Add(_modernTitle); _modernHeaderContent.Controls.Add(headerTools);
        _modernHeader.Controls.Add(_modernHeaderContent);

        _modernFooter = new BufferedPanel { Dock = DockStyle.Bottom, Height = 87, Padding = new Padding(14, 0, 14, 10), BackColor = ModernTheme.Background };
        _modernFooterContent = new BufferedPanel { Dock = DockStyle.Fill, BackColor = ModernTheme.Background };
        var progress = new BufferedPanel { Dock = DockStyle.Top, Height = 28, BackColor = ModernTheme.Background };
        _modernTime = new Label { Dock = DockStyle.Left, Width = 74, ForeColor = ModernTheme.Muted, Text = "00:00:00", TextAlign = ContentAlignment.MiddleLeft };
        _modernDuration = new Label { Dock = DockStyle.Right, Width = 74, ForeColor = ModernTheme.Muted, Text = "00:00:00", TextAlign = ContentAlignment.MiddleRight };
        _modernSeek = new ModernSeek { Dock = DockStyle.Fill, BackColor = ModernTheme.Background };
        InitializeModernScrubbing();
        progress.Controls.Add(_modernSeek); progress.Controls.Add(_modernTime); progress.Controls.Add(_modernDuration);
        var row = new BufferedPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0), BackColor = ModernTheme.Background };
        var playback = new ToolbarFlowPanel { Dock = DockStyle.Left, Width = 224, WrapContents = false };
        _modernPlay = MakeButton("播放 / 暂停", "\uE768", (_, _) => Player.CommandV("cycle", "pause"));
        _modernPlay.Accent = true;
        _modernSpeed = MakeButton("1.00 ×", "", (_, _) => ShowSpeed(), 78);
        _modernSpeed.SeparatorGroup = "speed";
        playback.Controls.AddRange([
            _modernPlay,
            MakeButton("上一个视频", "\uE892", (_, _) => Player.CommandV("playlist-prev", "weak"), icon: ModernIcon.PreviousVideo),
            MakeButton("下一个视频", "\uE893", (_, _) => Player.CommandV("playlist-next", "weak"), icon: ModernIcon.NextVideo),
            _modernSpeed
        ]);
        var right = new ToolbarFlowPanel { Dock = DockStyle.Right, Width = 180, WrapContents = false };
        _modernVolume = MakeButton("音量", "\uE767", (_, _) => ShowVolume());
        right.Controls.AddRange([
            MakeButton("字幕 / 音轨", "\uE8F2", (_, _) => ShowTracks()), _modernVolume,
            MakeButton("全屏", "\uE740", (_, _) => Player.SetPropertyBool("fullscreen", !Player.Fullscreen)),
            MakeButton("编辑工具栏", "\uE70F", (_, _) => ShowToolbarEditor())
        ]);
        _modernTools = new ToolbarPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 0, 0), LeadingDivider = true, BackColor = ModernTheme.Background };
        _moreButton = MakeButton("更多快捷按钮", "\uE712", (_, _) => ShowMoreTools());
        row.Controls.Add(_modernTools); row.Controls.Add(right); row.Controls.Add(playback);
        _modernFooterContent.Controls.Add(row); _modernFooterContent.Controls.Add(progress);
        _modernFooter.Controls.Add(_modernFooterContent);
        _videoSurface = new VideoSurface { Dock = DockStyle.Fill, BackColor = Color.Black, AllowDrop = true, InputHandler = ForwardVideoInput };
        _emptyMessage = new Label { Text = "拖入视频，或点击右上角打开", ForeColor = ModernTheme.Muted, Font = new Font("Microsoft YaHei UI", 12), AutoSize = true, BackColor = Color.Black };
        _videoSurface.Controls.Add(_emptyMessage);
        _videoSurface.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        _videoSurface.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) LoadModernFiles(files); };
        _videoSurface.Resize += (_, _) => { _emptyMessage.Location = new Point(Math.Max(10, (_videoSurface.Width - _emptyMessage.Width) / 2), Math.Max(10, (_videoSurface.Height - _emptyMessage.Height) / 2)); };
        InitializePlaylistPane();
        Controls.Add(_modernBody); Controls.Add(_modernFooter); Controls.Add(_modernHeader);
        InitializeWindowResizeGrips();
        TrackFullscreenChromeInput(_modernBody);
        TrackFullscreenChromeInput(_modernFooter);
        TrackFullscreenChromeInput(_modernHeader);
        _videoSurface.MouseCaptureChanged += (_, _) => { if (!_videoSurface.Capture) CancelVideoPan(); };
        _modernTools.Resize += (_, _) => LayoutToolbar();
        RebuildToolbar();
        _modernTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _modernTimer.Tick += (_, _) => RefreshModernState();
    }

    void InitializeModernPlayer()
    {
        Player.Border = false;
        Player.TitleBar = false;
        Player.KeepaspectWindow = false;
        Player.SetPropertyString("osc", "no");
        Player.SetPropertyBool("border", false);
        Player.SetPropertyBool("title-bar", false);
        Player.SetPropertyBool("keepaspect-window", false);
        ApplyModernCursorMode();
        ApplyModernVolumeOptions();
        InitializeModernPicture();
        if (string.IsNullOrWhiteSpace(Player.GetPropertyString("input-ipc-server")))
            Player.SetPropertyString("input-ipc-server", @"\\.\pipe\FrameFlow");
        ConfigureModernSpeedAudio();
        Player.ObservePropertyBool("audio-pitch-correction", _ => Ui(ConfigureModernSpeedAudio));
        Player.SetPropertyDouble("speed", _modernSettings.RememberSpeed ? (double)_modernSettings.Speed : 1);
        Player.SetPropertyString("user-data/modern/tools", JsonSerializer.Serialize(ToolbarCatalog.All.Select(t => new {
            id = t.Id, title = t.Title, category = t.Category, value = t.Value, parameter = t.Parameter
        })));
        Player.ObservePropertyDouble("speed", speed => Ui(() => {
            if (_heldDirection != 0) return; // Temporary navigation speed is not a saved preference.
            if (Math.Abs(speed - Player.GetPropertyDouble("speed")) > .0001) return; // Ignore queued temporary values after release.
            if (!double.IsFinite(speed) || speed < (double)ModernSettings.MinimumSpeed || speed > (double)ModernSettings.MaximumSpeed || _modernClosing) return;
            ConfigureModernSpeedAudio();
            _modernSettings.Speed = ModernSettings.NormalizeSpeed((decimal)speed);
            _modernSpeed.Text = speed.ToString("0.00", CultureInfo.InvariantCulture) + " ×";
            _modernSpeed.AccessibleName = _modernSpeed.Text;
            _modernSpeed.Invalidate();
            if (_speedPopup != null) PublishSpeedPopup(_speedPopup.Visible);
        }));
        // Set a file-local start before playback can begin. A post-load seek
        // from an external client must never be overwritten by a queued UI task.
        InitializeSharedProgress();
        InitializeModernShortcuts();
        PublishModernMouseActions();
        Player.FileLoaded += () => Ui(ModernFileLoaded);
        Player.StartFile += () => Ui(() => {
            EndHeldNavigation(false);
            CancelModernScrubbing();
        });
        InitializeNavigationFeedback();
        ApplyPlaylistMode(_modernSettings.PlaylistMode, false);
        Shown += (_, _) => {
            _modernTimer.Start(); RefreshModernState();
            SetPlaylistVisible(_modernSettings.PlaylistVisible);
            _emptyMessage.BringToFront();
        };
    }

    void Ui(Action action)
    {
        if (IsDisposed || _modernClosing) return;
        if (InvokeRequired) { try { BeginInvoke(() => { if (!_modernClosing) action(); }); } catch (InvalidOperationException) { } }
        else action();
    }
    ModernButton MakeButton(string title, string glyph, EventHandler click, int width = 40, ModernIcon icon = ModernIcon.FontGlyph)
    {
        var button = new ModernButton(title, glyph, width) { IconOnly = width <= 64, Icon = icon }; button.Click += click;
        button.Click += (_, _) => {
            // A toolbar click must not leave playback keys on a Button. Dialogs
            // and popups retain their own focus and keyboard editing behavior.
            if (!_modernClosing && button.Focused && Enabled && _fullscreenOpenMenus == 0 &&
                !(_contextMenuIsReady && ContextMenu.IsOpen) && !HasFullscreenDialog()) _videoSurface?.Focus();
        };
        button.MouseEnter += (_, _) => ShowCursor();
        _modernTips.SetToolTip(button, title); return button;
    }
    void ApplyModernCursorMode()
    {
        _cursorAutohide = _modernSettings.AutoHideCursor ? 1000 : 0;
        Player.SetPropertyString("cursor-autohide", _modernSettings.AutoHideCursor ? "1000" : "no");
        ShowCursor();
    }
    void DragTitle(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || sender is not Control control) return;
        CancelTitleDrag();
        if (e.Clicks == 2) {
            ExecuteDoubleClickAction(_modernSettings.TitleBarDoubleClickAction);
            return;
        }
        _titleDragSource = control;
        _titleDragStart = control.PointToScreen(e.Location);
        control.Capture = true;
    }
    void MoveTitle(object? sender, MouseEventArgs e)
    {
        if (sender is not Control control || _titleDragSource != control || e.Button != MouseButtons.Left) return;
        if (_modernFullscreen) { CancelTitleDrag(); return; }
        var point = control.PointToScreen(e.Location);
        var threshold = SystemInformation.DragSize;
        if (Math.Abs(point.X - _titleDragStart.X) < threshold.Width / 2 &&
            Math.Abs(point.Y - _titleDragStart.Y) < threshold.Height / 2) return;
        // Start the native move only after an actual drag. Starting it on every
        // click lets Windows consume the second click and maximize on its own.
        CancelTitleDrag();
        WinApi.ReleaseCapture(); WinApi.PostMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
    }
    void CancelTitleDrag()
    {
        var control = _titleDragSource;
        _titleDragSource = null;
        if (control != null) control.Capture = false;
    }
    void ExecuteDoubleClickAction(string action)
    {
        switch (action) {
            case "fullscreen":
                bool enabled = !_modernFullscreen;
                Player.SetPropertyBool("fullscreen", enabled);
                SetModernFullscreen(enabled);
                break;
            case "maximize": ToggleMaximize(); break;
            case "pause": Player.CommandV("cycle", "pause"); break;
        }
    }
    void ToggleMaximize()
    {
        bool restore = !_modernFullscreen && WindowState == FormWindowState.Maximized;
        if (_modernFullscreen) {
            Player.SetPropertyBool("fullscreen", false);
            SetModernFullscreen(false);
        }
        WindowState = restore ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    void ModernFileLoaded()
    {
        ApplyModernNightMode();
        EndHeldNavigation(false);
        CancelModernScrubbing();
        ResetNavigationFeedback();
        ConfigureModernSpeedAudio();
        var path = Player.GetPropertyString("path");
        CancelVideoPan();
        if (_videoViewGestureUsed && path != _currentFile) ResetVideoView(false);
        _currentFile = path; _currentPosition = 0;
        EnsureCurrentFolderPlaylist(path);
        _modernTitle.Text = Player.GetPropertyString("filename");
        _emptyMessage.Visible = false;
        if (_modernSettings.RememberSpeed) Player.SetPropertyDouble("speed", (double)_modernSettings.Speed);
        RefreshPlaylistPane();
        RefreshModernState();
    }

    static string TimeText(double seconds) => TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0)).ToString(@"hh\:mm\:ss");
    void RefreshModernState()
    {
        if (_modernClosing || Player.Handle == IntPtr.Zero) return;
        RefreshFullscreenChrome();
        var duration = Player.GetPropertyDouble("duration");
        var position = Player.GetPropertyDouble("time-pos");
        if (Player.GetPropertyString("path").Length == 0 && _currentFile.Length > 0) {
            CancelModernScrubbing();
            ResetNavigationFeedback();
            _currentFile = ""; _currentPosition = 0; _modernTitle.Text = ""; _emptyMessage.Visible = true;
        }
        if (position >= 0 && double.IsFinite(position)) _currentPosition = position;
        if (!_modernInteractiveResize) {
            if (_heldDirection == 0) UpdateModernSeekPosition(_currentPosition, duration);
            _modernTime.Text = TimeText(_modernSeek.Position); _modernDuration.Text = TimeText(duration);
            // Font metrics can change independently of the initial DPI scaling.
            // Reserve the measured width of the complete clock, including seconds.
            int timePadding = (int)Math.Ceiling(12 * DeviceDpi / 96d);
            _modernTime.Width = TextRenderer.MeasureText(_modernTime.Text, _modernTime.Font).Width + timePadding;
            _modernDuration.Width = TextRenderer.MeasureText(_modernDuration.Text, _modernDuration.Font).Width + timePadding;
            _modernPlay.Glyph = Player.Paused ? "\uE768" : "\uE769"; _modernPlay.Invalidate();
            _modernVolume.Glyph = Player.GetPropertyBool("mute") ? "\uE74F" : "\uE767"; _modernVolume.Invalidate();
            UpdatePinButton();
            foreach (var pair in _toolButtons) {
                bool active = pair.Item.Id switch {
                    "loop-file" => Player.GetPropertyString("loop-file") == "inf",
                    "loop-playlist" => Player.GetPropertyString("loop-playlist") == "inf",
                    "ontop" => Player.GetPropertyBool("ontop"),
                    "fullscreen" => Player.Fullscreen,
                    "playlist" => _modernSettings.PlaylistVisible,
                    "mute" => Player.GetPropertyBool("mute"),
                    "sub-toggle" => Player.GetPropertyBool("sub-visibility"),
                    _ => false
                };
                if (pair.Button.Checked != active) { pair.Button.Checked = active; pair.Button.Invalidate(); }
            }
        }
        _stateSnapshot = new {
            path = _currentFile, filename = Player.GetPropertyString("filename"), time = _currentPosition, duration,
            pause = Player.Paused, speed = Player.GetPropertyDouble("speed"), volume = Player.GetPropertyDouble("volume"),
            width = Player.GetPropertyInt("width"), height = Player.GetPropertyInt("height"), fps = Player.GetPropertyDouble("container-fps")
        };
        if (++_saveTick >= 20) { _saveTick = 0; SaveCurrentPosition(); SaveModernSettings(); }
        if (_saveTick % 4 == 0) {
            Player.SetPropertyString("user-data/modern/state", JsonSerializer.Serialize(_stateSnapshot));
            PublishModernWindowState();
            if (_playlistPane.Visible) RefreshPlaylistPane();
        }
        if (_toast != null && DateTime.UtcNow > _toastExpires) { _toast.Dispose(); _toast = null; }
    }
    void SaveModernSettings()
    {
        try { lock (_resumePositionLock) _modernSettings.Save(_modernSettingsPath); }
        catch (Exception ex) { Terminal.WriteError(ex.Message); }
    }
    void LoadModernFiles(string[] files)
    {
        SaveCurrentPosition(); SaveModernSettings();
        Player.LoadFiles(files, false, false, new Dictionary<string, string> { ["start"] = "none", ["resume-playback"] = "yes" });
    }
    internal ModernSettings ModernPreferences => _modernSettings;
    internal void SaveModernPreferences()
    {
        EndHeldNavigation(false);
        PublishHeldNavigation();
        ApplyModernCursorMode();
        ApplyModernVolumeOptions();
        if (!_modernSettings.CtrlMouseVideoView) CancelVideoPan();
        _wheelBindingRemainder = _ctrlWheelBindingRemainder = 0;
        PublishModernMouseActions();
        if (!_modernSettings.ShowNavigationFeedback) ClearNavigationFeedback();
        RefreshFullscreenChrome();
        SaveModernSettings();
    }
    void OpenVideo()
    {
        using var picker = new OpenFileDialog { Title = "打开视频", Multiselect = true, Filter = "视频和音频|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv;*.m4v;*.ts;*.flv;*.mp3;*.flac;*.wav|所有文件|*.*" };
        if (picker.ShowDialog(this) == DialogResult.OK) LoadModernFiles(picker.FileNames);
    }

    static string ToolGlyph(string id) => ToolbarCatalog.Find(id)?.Glyph ?? "\uE712";
    void RebuildToolbar()
    {
        _modernTools.SuspendLayout();
        try {
            foreach (var pair in _toolButtons) pair.Button.Dispose();
            _toolButtons.Clear(); _modernTools.Controls.Clear();
            // Match the existing fixed buttons, including whatever scaling the
            // host has already applied. DeviceDpi alone can double-scale newly
            // added controls in an already-created WinForms window.
            double scale = _moreButton.Width / 40d;
            foreach (var item in _modernSettings.Toolbar.Take(ModernSettings.LegacyToolbarItemLimit)) {
                int width = _modernSettings.ShowButtonLabels
                    ? (int)Math.Clamp(TextRenderer.MeasureText(item.Title, _moreButton.Font).Width + 48 * scale, 100 * scale, 175 * scale)
                    : _moreButton.Width;
                var button = MakeButton(item.Title, ToolGlyph(item.Id), (_, _) => ExecuteTool(item), width);
                button.IconOnly = !_modernSettings.ShowButtonLabels;
                button.Height = _moreButton.Height;
                button.Margin = _moreButton.Margin;
                button.Icon = item.Id switch { "frame-back" => ModernIcon.PreviousFrame, "frame-forward" => ModernIcon.NextFrame, _ => ModernIcon.FontGlyph };
                button.SeparatorGroup = item.Id is "frame-back" or "frame-forward" ? "frames" : ToolbarCatalog.Find(item.Id)?.Category ?? "";
                button.Checked = item.Id == "night-mode" && _modernSettings.NightMode;
                _toolButtons.Add((item, button)); _modernTools.Controls.Add(button);
            }
            _modernTools.Controls.Add(_moreButton);
        } finally { _modernTools.ResumeLayout(false); }
        LayoutToolbar();
    }
    void LayoutToolbar(bool restoringHiddenContents = false)
    {
        if (_modernTools == null || _moreButton == null || (_modernInteractiveResize && !restoringHiddenContents)) return;
        var area = _modernTools.DisplayRectangle;
        var fit = ToolbarLayout.Fit(area.Width,
            _toolButtons.Select(pair => pair.Button.Width + pair.Button.Margin.Horizontal).ToArray(),
            _moreButton.Width + _moreButton.Margin.Horizontal, ModernSettings.MaxToolbarItems);
        _modernTools.SuspendLayout();
        try {
            int x = area.Left;
            for (int i = 0; i < _toolButtons.Count; i++) {
                var button = _toolButtons[i].Button;
                bool visible = i < fit.VisibleCount;
                if (visible) {
                    button.Location = new Point(x + button.Margin.Left, area.Top + button.Margin.Top);
                    x += button.Width + button.Margin.Horizontal;
                }
                button.Visible = visible;
            }
            _moreButton.Location = new Point(x + _moreButton.Margin.Left, area.Top + _moreButton.Margin.Top);
            _moreButton.Visible = fit.Overflow;
            _toolbarVisibleCount = fit.VisibleCount;
            _toolbarHasOverflow = fit.Overflow;
            _toolbarLayoutPasses++;
        } finally { _modernTools.ResumeLayout(false); }
        _modernTools.Invalidate();
    }
    void ShowMoreTools()
    {
        var menu = new ContextMenuStrip { BackColor = ModernTheme.Surface, ForeColor = ModernTheme.Text };
        TrackFullscreenChromeMenu(menu);
        foreach (var pair in _toolButtons.Skip(_toolbarVisibleCount)) {
            var entry = menu.Items.Add(pair.Item.Title); entry.Enabled = pair.Button.Enabled;
            entry.Click += (_, _) => ExecuteTool(pair.Item);
        }
        menu.Closed += (_, _) => menu.Dispose(); menu.Show(_moreButton, new Point(0, -menu.PreferredSize.Height));
    }
    void ExecuteTool(ToolbarItem item)
    {
        AllowNavigationInput();
        try {
            switch (item.Id) {
                case "frame-back": NavigateVideo("上一帧", "frame-back-step"); break;
                case "frame-forward": NavigateVideo("下一帧", "frame-step"); break;
                case "seek-back":
                case "seek-forward":
                    foreach (var command in ToolbarCatalog.Commands(item))
                        NavigateVideo($"{(item.Id == "seek-back" ? "步退" : "步进")} {Math.Abs(double.Parse(command[1], CultureInfo.InvariantCulture)):0.##} 秒", command);
                    break;
                case "seek-start": NavigateVideo("回到开头", "seek", "0", "absolute+exact"); break;
                case "chapter-back":
                case "chapter-forward":
                    foreach (var command in ToolbarCatalog.Commands(item)) NavigateVideo(item.Title, command);
                    break;
                case "screenshot": ShowScreenshot(true); break;
                case "screenshot-video": ShowScreenshot(false); break;
                case "bookmark": AddBookmark(); break;
                case "bookmarks": ShowBookmarks(); break;
                case "ab-loop": Player.CommandV("ab-loop"); ShowToast("AB 循环：依次设置起点、终点、取消"); break;
                case "ab-clear": Player.SetPropertyString("ab-loop-a", "no"); Player.SetPropertyString("ab-loop-b", "no"); ShowToast("已取消 AB 循环"); break;
                case "timestamp": Clipboard.SetText($"{_currentFile}\n{TimeText(_currentPosition)}"); ShowToast("已复制视频路径和时间戳"); break;
                case "speed": SetSpeed((decimal)item.Value); break;
                case "speed-up": SetSpeed(_modernSettings.Speed + _modernSettings.SpeedStep); break;
                case "speed-down": SetSpeed(_modernSettings.Speed - _modernSettings.SpeedStep); break;
                case "speed-reset": SetSpeed(1); break;
                case "jump-time": ShowJumpTime(); break;
                case "zoom-reset": ResetVideoView(); break;
                case "picture": ShowPicture(); break;
                case "night-mode": SetModernNightMode(!_modernSettings.NightMode); ShowToast(_modernSettings.NightMode ? "夜间护眼已开启" : "夜间护眼已关闭"); break;
                case "picture-reset":
                    SetModernNightMode(false); SetModernNightStrength(ModernSettings.DefaultNightStrength); SetModernBrightness(0);
                    foreach (var property in new[] { "video-zoom", "video-pan-x", "video-pan-y", "video-rotate", "brightness", "contrast", "saturation", "gamma" }) Player.CommandV("set", property, "0");
                    Player.SetPropertyDouble("video-aspect-override", -1); ShowToast("已还原画面设置"); break;
                case "sub-load": LoadSubtitle(); break;
                case "media-info": ShowMediaInfo(); break;
                case "playlist": ShowPlaylist(); break;
                case "open-video": OpenVideo(); break;
                case "settings": ShowModernSettings(); break;
                case "window-reset": ResetModernWindowSize(); break;
                case "ontop": Player.SetPropertyBool("ontop", !Player.GetPropertyBool("ontop")); break;
                default:
                    foreach (var command in ToolbarCatalog.Commands(item)) Player.CommandV(command);
                    break;
            }
        } catch (Exception ex) { ShowToast(ex.Message); }
    }
    void AddBookmark()
    {
        if (string.IsNullOrEmpty(_currentFile)) { ShowToast("请先打开视频"); return; }
        var title = PromptText("添加书签", "书签名称", TimeText(_currentPosition));
        if (title == null) return;
        _modernSettings.Bookmarks.Add(new(_currentFile, _currentPosition, title)); SaveModernSettings(); ShowToast("书签已保存");
    }
    void SetSpeed(decimal speed)
    {
        EndHeldNavigation(false);
        AllowNavigationInput();
        ConfigureModernSpeedAudio();
        _modernSettings.Speed = ModernSettings.NormalizeSpeed(speed);
        QueueSliderProperty("speed", (double)_modernSettings.Speed);
    }
    void ShowToast(string text)
    {
        Ui(() => {
            if (_navigationTimer != null && _currentFile.Length > 0 && Player.Handle != IntPtr.Zero) {
                ShowOperationFeedback("notification", text);
                return;
            }
            _toast?.Dispose();
            _toast = new Label { Text = text, AutoSize = true, MaximumSize = new Size(Math.Max(200, Width - 60), 0), Padding = new Padding(12, 7, 12, 7), BackColor = ModernTheme.Hover, ForeColor = ModernTheme.Text, Location = new Point(18, 52) };
            Controls.Add(_toast); _toast.BringToFront(); _toastExpires = DateTime.UtcNow.AddSeconds(3);
        });
    }
    void ShutdownModernUi()
    {
        EndModernWindowResize();
        EndHeldNavigation(false);
        _heldNavigationTimer?.Dispose();
        FlushSliderProperties();
        _sliderTimer?.Dispose();
        _seekPreviewTimer?.Dispose();
        _resizeFrameTimer?.Dispose();
        _resizeRevealTimer?.Stop();
        _resizeRevealTimer?.Dispose();
        var volumePopup = _volumePopup;
        volumePopup?.Close(); volumePopup?.Dispose();
        var picturePopup = _picturePopup;
        picturePopup?.Close(); picturePopup?.Dispose();
        FlushModernPictureChanges(); _pictureTimer?.Dispose();
        ClearNavigationFeedback();
        _navigationTimer?.Dispose();
        _modernTimer.Stop(); SaveCurrentPosition();
        RememberModernWindowSize();
        SaveModernSettings(); _modernClosing = true;
        var speedPopup = _speedPopup;
        speedPopup?.Close(); speedPopup?.Dispose();
    }

    void HandleModernToolMessage(string[] args)
    {
        if (args.Length < 2) return;
        var definition = ToolbarCatalog.Find(args[1]);
        if (definition == null) return;
        try {
            var item = definition.Item;
            if (args.Length > 2 && definition.Parameter != null) item = definition.Parameterize(double.Parse(args[2], CultureInfo.InvariantCulture));
            ExecuteTool(item);
        } catch (Exception ex) { ShowToast(ex.Message); }
    }
}
