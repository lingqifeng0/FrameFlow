using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using MpvNet.Windows.Modern;
using MpvNet.Windows.WPF;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    ToolStripDropDown? _picturePopup;
    System.Windows.Forms.Timer? _pictureTimer;
    bool _nightShaderPending, _pictureSavePending;
    long _pictureEditedAt;
    internal event Action? ModernPictureChanged;

    void InitializeModernPicture()
    {
        if (_modernSettings.HasSavedVideoBrightness)
            Player.SetPropertyInt("brightness", _modernSettings.VideoBrightness);
        else _modernSettings.VideoBrightness = Math.Clamp(Player.GetPropertyInt("brightness"), -100, 100);
        Player.ObservePropertyDouble("brightness", value => Ui(() => {
            if (!double.IsFinite(value) || Math.Abs(value - Player.GetPropertyDouble("brightness")) > .0001) return;
            int brightness = Math.Clamp((int)Math.Round(value), -100, 100);
            if (_modernSettings.VideoBrightness == brightness) return;
            _modernSettings.VideoBrightness = brightness;
            PictureChanged();
        }));
        ApplyModernNightMode();
    }

    internal void SetModernBrightness(int value)
    {
        value = Math.Clamp(value, -100, 100);
        if (_modernSettings.VideoBrightness == value && Player.GetPropertyInt("brightness") == value) return;
        _modernSettings.VideoBrightness = value;
        Player.SetPropertyInt("brightness", value);
        PictureChanged();
    }

    internal void SetModernNightMode(bool enabled)
    {
        _modernSettings.NightMode = enabled;
        _nightShaderPending = false;
        ApplyModernNightMode();
        PictureChanged();
    }

    internal void SetModernNightStrength(int value)
    {
        value = ModernSettings.NormalizeNightStrength(value);
        if (value == _modernSettings.NightStrength) return;
        _modernSettings.NightStrength = value;
        _nightShaderPending = _modernSettings.NightMode;
        PictureChanged();
    }

    void PictureChanged()
    {
        _pictureEditedAt = Environment.TickCount64;
        _pictureSavePending = true;
        _pictureTimer ??= CreatePictureTimer();
        _pictureTimer.Start();
        ModernPictureChanged?.Invoke();
        PublishModernPicture();
    }

    System.Windows.Forms.Timer CreatePictureTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) => {
            // Coalesce dragging to one latest shader change per tick. Persist
            // after the gesture settles, rather than writing JSON per pixel.
            if (_nightShaderPending) { _nightShaderPending = false; ApplyModernNightMode(); }
            if (_pictureSavePending && Environment.TickCount64 - _pictureEditedAt >= 300) {
                _pictureSavePending = false;
                SaveModernSettings();
            }
            if (!_nightShaderPending && !_pictureSavePending) timer.Stop();
        };
        return timer;
    }

    internal void FlushModernPictureChanges()
    {
        _pictureTimer?.Stop();
        if (_nightShaderPending) { _nightShaderPending = false; ApplyModernNightMode(); }
        if (_pictureSavePending) { _pictureSavePending = false; SaveModernSettings(); }
    }

    void ApplyModernNightMode()
    {
        if (Player.Handle == IntPtr.Zero) return;
        try {
            string? wanted = _modernSettings.NightMode && _modernSettings.NightStrength > 0
                ? NightShader.EnsureFile(Player.ConfigFolder, _modernSettings.NightStrength) : null;
            var shaders = Player.GetPropertyStringList("glsl-shaders");
            var updated = shaders.Where(path => !NightShader.IsOwned(Player.ConfigFolder, path)).ToList();
            if (wanted != null) updated.Add(wanted);
            // One atomic list update avoids displaying an untinted frame
            // between removing the previous strength and adding the new one.
            if (!shaders.SequenceEqual(updated)) Player.SetPropertyStringList("glsl-shaders", updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            _modernSettings.NightMode = false;
            var shaders = Player.GetPropertyStringList("glsl-shaders");
            var retained = shaders.Where(path => !NightShader.IsOwned(Player.ConfigFolder, path)).ToArray();
            if (!shaders.SequenceEqual(retained)) Player.SetPropertyStringList("glsl-shaders", retained);
            ModernPictureChanged?.Invoke();
            ShowToast("夜间护眼未能启用：" + ex.Message);
        }
        PublishModernPicture();
    }

    void PublishModernPicture()
    {
        if (Player.Handle == IntPtr.Zero) return;
        string renderer = Player.GetPropertyString("current-vo");
        foreach (var pair in _toolButtons.Where(pair => pair.Item.Id == "night-mode"))
            if (pair.Button.Checked != _modernSettings.NightMode) {
                pair.Button.Checked = _modernSettings.NightMode; pair.Button.Invalidate();
            }
        Player.SetPropertyString("user-data/modern/picture", JsonSerializer.Serialize(new {
            brightness = _modernSettings.VideoBrightness, nightMode = _modernSettings.NightMode,
            nightStrength = _modernSettings.NightStrength, renderer,
            supported = renderer is "gpu" or "gpu-next", visible = _picturePopup?.Visible == true
        }));
    }

    void ShowPicture()
    {
        if (_picturePopup is { IsDisposed: false }) { _picturePopup.Close(); return; }
        _volumePopup?.Close(); _speedPopup?.Close();
        int Px(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        var picture = new ModernPictureControl(this);
        var content = new ElementHost { Size = new Size(Px(350), Px(224)), Child = picture, BackColor = ModernTheme.Surface };
        Control anchor = _toolButtons.FirstOrDefault(pair => pair.Item.Id == "picture").Button ?? _modernVolume;
        var popup = CreateModernPopup(content, anchor);
        popup.Items[0].AutoSize = false;
        popup.Items[0].Size = content.Size;
        _picturePopup = popup;
        popup.AccessibleName = "画面调节";
        popup.Closed += (_, _) => {
            FlushModernPictureChanges();
            picture.Detach(); content.Child = null;
            _picturePopup = null;
            PublishModernPicture();
        };
        ShowModernPopup(popup, anchor);
        PublishModernPicture();
    }
}
