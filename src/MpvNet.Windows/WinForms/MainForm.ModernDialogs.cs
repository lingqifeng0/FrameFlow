using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    string? PromptText(string title, string label, string initial)
    {
        using var form = ModernTheme.Dialog(title, 420, 145);
        form.MinimumSize = new Size(360, 170); form.FormBorderStyle = FormBorderStyle.FixedDialog;
        var hint = ModernTheme.Label(label); hint.Location = new Point(18, 12);
        var field = new TextBox { Text = initial, Location = new Point(18, 44), Width = 380 }; ModernTheme.StyleInput(field);
        var ok = new ModernButton("保存", width: 80) { Location = new Point(318, 89), Accent = true };
        ok.Click += (_, _) => form.DialogResult = DialogResult.OK;
        form.AcceptButton = ok; form.Controls.AddRange([hint, field, ok]);
        return form.ShowDialog(this) == DialogResult.OK ? field.Text : null;
    }

    ToolStripDropDown CreateModernPopup(Control content, Control anchor)
    {
        int inset = (int)Math.Round(6 * DeviceDpi / 96d);
        var popup = new ToolStripDropDown { AutoClose = true, Padding = new Padding(inset),
            BackColor = content.BackColor, DropShadowEnabled = true, AccessibleName = anchor.AccessibleName };
        popup.Items.Add(new ToolStripControlHost(content) { Margin = Padding.Empty, Padding = Padding.Empty });
        // Keep the dropdown alive until the anchor's Click handler runs. Otherwise
        // automatic dismissal happens before Click, and Click immediately reopens it.
        popup.Closing += (_, e) => {
            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked &&
                anchor.RectangleToScreen(anchor.ClientRectangle).Contains(MousePosition)) e.Cancel = true;
        };
        TrackFullscreenChromeMenu(popup);
        popup.Closed += (_, _) => {
            // Close still updates the dropdown's HWND after raising Closed.
            // Disposing inside that event can recreate a disposed handle. Normal
            // dismissal is deferred; ShutdownModernUi disposes after Close returns.
            if (!IsDisposed && !_modernClosing) BeginInvoke(() => popup.Dispose());
        };
        return popup;
    }

    void ShowModernPopup(ToolStripDropDown popup, Control anchor)
    {
        int gap = (int)Math.Round(6 * DeviceDpi / 96d);
        Size size = popup.GetPreferredSize(Size.Empty);
        Point point = anchor.PointToScreen(new Point((anchor.Width - size.Width) / 2, -size.Height - gap));
        Rectangle area = Screen.FromControl(this).WorkingArea;
        point.X = Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - size.Width));
        point.Y = Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        popup.Show(point);
    }

    void PublishSpeedPopup(bool visible)
    {
        if (Player.Handle == IntPtr.Zero) return;
        var bounds = _speedPopup?.Bounds ?? Rectangle.Empty;
        Player.SetPropertyString("user-data/modern/speed", JsonSerializer.Serialize(new {
            visible, speed = Player.GetPropertyDouble("speed"), minimum = ModernSettings.MinimumSpeed, maximum = ModernSettings.MaximumSpeed,
            bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }
        }));
    }

    void ShowSpeed()
    {
        if (_speedPopup is { IsDisposed: false }) { _speedPopup.Close(); return; }
        _volumePopup?.Close();
        _picturePopup?.Close();
        int Px(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        var content = new BufferedPanel { Size = new Size(Px(280), Px(218)), BackColor = ModernTheme.Background };
        var title = ModernTheme.Label("播放速度", 11); title.Location = new Point(Px(12), Px(12));
        var value = new NumericUpDown { DecimalPlaces = 2, Minimum = ModernSettings.MinimumSpeed, Maximum = ModernSettings.MaximumSpeed, Increment = _modernSettings.SpeedStep, Value = _modernSettings.Speed, Location = new Point(Px(152), Px(12)), Width = Px(112) };
        value.AccessibleName = "自定义播放倍率"; ModernTheme.StyleInput(value);
        var slider = new TrackBar { Minimum = (int)(ModernSettings.MinimumSpeed * 100), Maximum = (int)(ModernSettings.MaximumSpeed * 100), Value = (int)(_modernSettings.Speed * 100), TickStyle = TickStyle.None, Location = new Point(Px(8), Px(58)), Width = Px(264), BackColor = ModernTheme.Background, AccessibleName = "拖动播放倍率" };
        bool updating = false;
        value.ValueChanged += (_, _) => { if (updating) return; updating = true; slider.Value = (int)(value.Value * 100); SetSpeed(value.Value); updating = false; };
        slider.ValueChanged += (_, _) => { if (updating) return; updating = true; value.Value = slider.Value / 100m; SetSpeed(value.Value); updating = false; };
        var stepTitle = ModernTheme.Label("每次调整"); stepTitle.Location = new Point(Px(12), Px(108));
        var step = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(Px(152), Px(102)), Width = Px(112) };
        step.Items.AddRange(["0.01", "0.05", "0.10"]); step.SelectedIndex = _modernSettings.SpeedStep == 0.01m ? 0 : _modernSettings.SpeedStep == 0.05m ? 1 : 2;
        ModernTheme.StyleInput(step);
        step.SelectedIndexChanged += (_, _) => { _modernSettings.SpeedStep = decimal.Parse(step.Text, CultureInfo.InvariantCulture); value.Increment = _modernSettings.SpeedStep; SaveModernSettings(); };
        var remember = new CheckBox { Text = "退出后记住播放速度", Checked = _modernSettings.RememberSpeed, ForeColor = ModernTheme.Text, AutoSize = true, Location = new Point(Px(12), Px(150)) };
        remember.CheckedChanged += (_, _) => { _modernSettings.RememberSpeed = remember.Checked; SaveModernSettings(); };
        var hint = ModernTheme.Label($"{ModernSettings.MinimumSpeed:0.00}–{ModernSettings.MaximumSpeed:0.00} 倍 · 调整立即生效", 9, true); hint.Location = new Point(Px(12), Px(186));
        content.Controls.AddRange([title, value, slider, stepTitle, step, remember, hint]);
        var popup = CreateModernPopup(content, _modernSpeed);
        _speedPopup = popup;
        popup.Closed += (_, _) => { FlushSliderProperties(); SaveModernSettings(); _speedPopup = null; PublishSpeedPopup(false); };
        ShowModernPopup(popup, _modernSpeed);
        PublishSpeedPopup(true);
    }

    void ShowVolume()
    {
        ToggleVolumePopup();
    }

    void ShowMediaInfo()
    {
        using var form = ModernTheme.Dialog("视频信息", 610, 460);
        var header = ModernTheme.Label("视频信息", 14); header.Location = new Point(22, 16);
        string Property(string name) { var value = Player.GetPropertyString(name); return string.IsNullOrEmpty(value) ? "—" : value; }
        string path = Player.GetPropertyString("path");
        string size = File.Exists(path) ? (new FileInfo(path).Length / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : "—";
        var fps = Player.GetPropertyDouble("container-fps");
        string info = $"文件名\t{Property("filename")}\r\n时长\t{TimeText(Player.GetPropertyDouble("duration"))}\r\n大小\t{size}\r\n\r\n" +
            $"分辨率\t{Property("video-params/w")} × {Property("video-params/h")}\r\n帧率\t{(fps > 0 ? fps.ToString("0.###", CultureInfo.InvariantCulture) : "—")} FPS\r\n视频编码\t{Property("video-codec")}\r\n像素格式\t{Property("video-params/pixelformat")}\r\n\r\n" +
            $"音频编码\t{Property("audio-codec-name")}\r\n声道\t{Property("audio-params/channels")}\r\n声道数\t{Property("audio-params/channel-count")}\r\n采样率\t{Property("audio-params/samplerate")} Hz\r\n\r\n文件位置\r\n{path}";
        var content = new TextBox { Multiline = true, ReadOnly = true, TabStop = false, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, Location = new Point(22, 61), Size = new Size(564, 320), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Text = info };
        ModernTheme.StyleInput(content); content.BackColor = ModernTheme.Background;
        var reveal = new ModernButton("打开所在文件夹", "\uE8E5", 172) { Location = new Point(22, 401), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Accent = true, Enabled = File.Exists(path) };
        reveal.Click += (_, _) => {
            if (!File.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + Path.GetFullPath(path) + "\"") { UseShellExecute = true });
        };
        var copy = new ModernButton("复制信息", width: 108) { Location = new Point(478, 401), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        copy.Click += (_, _) => Clipboard.SetText(info);
        form.Controls.AddRange([header, content, reveal, copy]); form.ShowDialog(this);
    }

    void ShowBookmarks()
    {
        using var form = ModernTheme.Dialog("视频书签", 620, 430);
        var items = _modernSettings.Bookmarks.Where(b => b.File == _currentFile).ToList();
        var list = new ListBox { Location = new Point(18, 18), Size = new Size(584, 320), BorderStyle = BorderStyle.None }; ModernTheme.StyleInput(list);
        foreach (var b in items) list.Items.Add($"{TimeText(b.Time)}   {b.Title}");
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) { NavigateVideo("书签定位", "seek", items[list.SelectedIndex].Time.ToString(CultureInfo.InvariantCulture), "absolute+exact"); form.Close(); } };
        var add = new ModernButton("添加书签", "\uE710", 130) { Location = new Point(18, 357) };
        add.Click += (_, _) => { form.Close(); AddBookmark(); };
        var delete = new ModernButton("删除选中", width: 120) { Location = new Point(480, 357) };
        delete.Click += (_, _) => { if (list.SelectedIndex < 0) return; int index = list.SelectedIndex; _modernSettings.Bookmarks.Remove(items[index]); items.RemoveAt(index); list.Items.RemoveAt(index); SaveModernSettings(); };
        form.Controls.AddRange([list, add, delete]); form.ShowDialog(this);
    }

    void ShowTracks()
    {
        var menu = new ContextMenuStrip { BackColor = ModernTheme.Surface, ForeColor = ModernTheme.Text };
        var subtitle = new ToolStripMenuItem("字幕"); var audio = new ToolStripMenuItem("音轨");
        var none = subtitle.DropDownItems.Add("关闭字幕"); none.Click += (_, _) => Player.SetPropertyString("sid", "no");
        int count = Player.GetPropertyInt("track-list/count");
        for (int i = 0; i < count; i++) {
            var type = Player.GetPropertyString($"track-list/{i}/type"); if (type is not ("audio" or "sub")) continue;
            var id = Player.GetPropertyString($"track-list/{i}/id");
            var title = Player.GetPropertyString($"track-list/{i}/title"); var lang = Player.GetPropertyString($"track-list/{i}/lang");
            var item = (type == "audio" ? audio : subtitle).DropDownItems.Add($"{id}   {title} {lang}".Trim());
            item.Click += (_, _) => Player.SetPropertyString(type == "audio" ? "aid" : "sid", id);
        }
        var load = subtitle.DropDownItems.Add("加载字幕文件…");
        load.Click += (_, _) => LoadSubtitle();
        menu.Items.AddRange([subtitle, audio]); menu.Closed += (_, _) => menu.Dispose(); menu.Show(Cursor.Position);
    }

    void LoadSubtitle()
    {
        using var picker = new OpenFileDialog { Title = "加载字幕", Filter = "字幕|*.srt;*.ass;*.ssa;*.vtt;*.sub|所有文件|*.*" };
        if (picker.ShowDialog(this) == DialogResult.OK) Player.CommandV("sub-add", picker.FileName);
    }

    void ShowJumpTime()
    {
        var input = PromptText("跳到指定时间", "输入 时:分:秒，或秒数", TimeText(_currentPosition));
        if (input == null) return;
        double seconds;
        if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) {
            if (!TimeSpan.TryParseExact(input, new[] { @"h\:mm\:ss", @"hh\:mm\:ss", @"m\:ss", @"mm\:ss" }, CultureInfo.InvariantCulture, out var time)) { ShowToast("请输入 00:12:30 或 750 这样的时间"); return; }
            seconds = time.TotalSeconds;
        }
        if (!double.IsFinite(seconds) || seconds < 0) { ShowToast("时间无效"); return; }
        NavigateVideo("定位", "seek", seconds.ToString(CultureInfo.InvariantCulture), "absolute+exact");
    }

    void ShowModernSettings()
    {
        _picturePopup?.Close();
        MpvNet.GuiCommand.Current.Commands["show-conf-editor"].Invoke(Array.Empty<string>());
    }

}
