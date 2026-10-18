using System.Drawing;
using System.Windows.Forms;
using System.Text.Json;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    ToolStripDropDown? _volumePopup;
    VolumeSlider? _volumeSlider;
    Label? _volumePercent;
    ModernButton? _volumeMute;
    CheckBox? _volumeBoost;

    void UpdatePinButton()
    {
        if (_modernPin == null) return;
        bool enabled = Player.GetPropertyBool("ontop");
        if (_modernPin.Checked == enabled) return;
        _modernPin.Checked = enabled; _modernPin.Invalidate();
        _modernTips.SetToolTip(_modernPin, enabled ? "取消窗口置顶（Pin）" : "窗口置顶（Pin）");
    }

    void ApplyModernVolumeOptions()
    {
        Player.SetPropertyInt("volume-max", _modernSettings.VolumeMaximum);
        if (_sliderProperties.TryGetValue("volume", out double pending)) QueueSliderProperty("volume", pending);
        if (_sliderTargets.TryGetValue("volume", out var target) && target.Value > _modernSettings.VolumeMaximum)
            _sliderTargets["volume"] = (_modernSettings.VolumeMaximum, Environment.TickCount64);
        double volume = Player.GetPropertyDouble("volume");
        if (volume > _modernSettings.VolumeMaximum)
            Player.SetPropertyDouble("volume", _modernSettings.VolumeMaximum);
        UpdateVolumePopup();
        PublishVolumePopup(_volumePopup?.Visible == true);
    }

    void ToggleVolumePopup()
    {
        if (_volumePopup is { IsDisposed: false }) { _volumePopup.Close(); return; }
        _speedPopup?.Close();
        _picturePopup?.Close();
        int Px(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        var content = new BufferedPanel { Size = new Size(Px(144), Px(284)), BackColor = ModernTheme.Surface };
        _volumePercent = new Label { TextAlign = ContentAlignment.MiddleCenter, ForeColor = ModernTheme.Text,
            Font = new Font("Microsoft YaHei UI", 10), Bounds = new Rectangle(0, Px(8), content.Width, Px(28)) };
        _volumeSlider = new VolumeSlider { Bounds = new Rectangle(Px(45), Px(38), Px(54), Px(155)),
            Maximum = _modernSettings.VolumeMaximum,
            Value = Math.Clamp(Player.GetPropertyInt("volume"), 0, _modernSettings.VolumeMaximum), BackColor = ModernTheme.Surface };
        _volumeSlider.Changed += value => {
            AllowNavigationInput();
            if (_volumePercent != null) _volumePercent.Text = $"{value:0.#}%";
            QueueSliderProperty("volume", value);
        };
        _volumeSlider.Released += FlushSliderProperties;
        _volumeMute = MakeButton("静音 / 恢复声音", "\uE767", (_, _) => {
            AllowNavigationInput(); Player.SetPropertyBool("mute", !Player.GetPropertyBool("mute"));
        }, 40);
        _volumeMute.Bounds = new Rectangle(Px(52), Px(201), Px(40), Px(36));
        _volumeMute.IconOnly = true;
        _volumeBoost = new CheckBox { Text = "声音增强", Checked = _modernSettings.VolumeBoost,
            ForeColor = ModernTheme.Text, Font = new Font("Microsoft YaHei UI", 9),
            Bounds = new Rectangle(Px(8), Px(242), Px(132), Px(30)), AccessibleName = "声音增强（可调至 300%）" };
        _modernTips.SetToolTip(_volumeBoost, "开启后可拖动至 300%，当前音量保持不变。");
        _volumeBoost.CheckedChanged += (_, _) => {
            if (_volumeBoost == null || _modernSettings.VolumeBoost == _volumeBoost.Checked) return;
            AllowNavigationInput();
            _modernSettings.VolumeBoost = _volumeBoost.Checked;
            ApplyModernVolumeOptions(); SaveModernSettings();
        };
        content.Controls.AddRange([_volumePercent, _volumeSlider, _volumeMute, _volumeBoost]);
        var popup = CreateModernPopup(content, _modernVolume);
        _volumePopup = popup;
        popup.Closed += (_, _) => {
            FlushSliderProperties();
            _volumePopup = null; _volumeSlider = null; _volumePercent = null; _volumeMute = null; _volumeBoost = null;
            PublishVolumePopup(false);
        };
        UpdateVolumePopup();
        ShowModernPopup(popup, _modernVolume);
        PublishVolumePopup(true);
    }

    void UpdateVolumePopup()
    {
        if (_volumePopup == null || _volumePopup.IsDisposed) return;
        double volume = SliderDisplayValue("volume", Player.GetPropertyDouble("volume"));
        bool muted = Player.GetPropertyBool("mute");
        if (_volumePercent != null) _volumePercent.Text = $"{volume:0.#}%";
        if (_volumeSlider != null) {
            _volumeSlider.Maximum = _modernSettings.VolumeMaximum;
            if (!_volumeSlider.Capture) _volumeSlider.Value = (int)Math.Round(Math.Clamp(volume, 0, _volumeSlider.Maximum));
            _volumeSlider.Invalidate();
        }
        if (_volumeBoost != null && _volumeBoost.Checked != _modernSettings.VolumeBoost) _volumeBoost.Checked = _modernSettings.VolumeBoost;
        if (_volumeMute != null) { _volumeMute.Glyph = muted ? "\uE74F" : "\uE767"; _volumeMute.Checked = muted; _volumeMute.Invalidate(); }
        PublishVolumePopup(_volumePopup.Visible);
    }

    void PublishVolumePopup(bool visible)
    {
        if (Player.Handle == IntPtr.Zero) return;
        var bounds = _volumePopup?.Bounds ?? Rectangle.Empty;
        Player.SetPropertyString("user-data/modern/volume", JsonSerializer.Serialize(new {
            visible, volume = Player.GetPropertyDouble("volume"), muted = Player.GetPropertyBool("mute"),
            maximum = _modernSettings.VolumeMaximum, extended = _modernSettings.VolumeBoost,
            wheelEnabled = _modernSettings.WheelAction == "volume", wheelAction = _modernSettings.WheelAction,
            bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }
        }));
    }

    sealed class VolumeSlider : Control
    {
        public int Value { get; set; }
        public int Maximum { get; set; } = 100;
        public event Action<int>? Changed;
        public event Action? Released;
        public VolumeSlider()
        {
            Name = "VolumePopupSlider"; AccessibleName = "音量滑杆"; AccessibleRole = AccessibleRole.Slider;
            SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            TabStop = true;
        }
        int Inset => Math.Max(8, DeviceDpi / 8);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float x = Width / 2f, top = Inset, bottom = Height - Inset;
            float y = bottom - (bottom - top) * Value / Maximum;
            using var track = new Pen(ModernTheme.Hover, Math.Max(3, DeviceDpi / 24));
            using var fill = new Pen(ModernTheme.Accent, track.Width);
            e.Graphics.DrawLine(track, x, top, x, bottom);
            e.Graphics.DrawLine(fill, x, y, x, bottom);
            float r = Math.Max(5, DeviceDpi / 19f);
            using var thumb = new SolidBrush(ModernTheme.Text);
            e.Graphics.FillEllipse(thumb, x-r, y-r, 2*r, 2*r);
        }
        void SetValue(int value)
        {
            value = Math.Clamp(value, 0, Maximum);
            if (Value == value) return;
            Value = value; Invalidate(); Changed?.Invoke(value);
        }
        void Seek(int y) => SetValue((int)Math.Round((double)Maximum * (Height-Inset-y) / Math.Max(1, Height-2*Inset)));
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus(); Capture = true; Seek(e.Y);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) Seek(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !Capture) return;
            Seek(e.Y); Capture = false;
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
            base.OnMouseWheel(e); SetValue(Value + e.Delta / 120 * 2);
        }
        protected override void OnMouseCaptureChanged(EventArgs e) {
            base.OnMouseCaptureChanged(e);
            if (!Capture) Released?.Invoke();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode) {
                case Keys.Up: case Keys.Right: SetValue(Value+2); break;
                case Keys.Down: case Keys.Left: SetValue(Value-2); break;
                case Keys.Home: SetValue(0); break;
                case Keys.End: SetValue(Maximum); break;
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
        }
        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);
    }
}
