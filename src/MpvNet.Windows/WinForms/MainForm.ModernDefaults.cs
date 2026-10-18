using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Xml.Serialization;
using System.Windows.Forms;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    internal string RestoreAllPlayerDefaults()
    {
        EndModernWindowResize(); FinishModernResizePreview(); EndHeldNavigation(false); CancelModernScrubbing();
        _sliderProperties.Clear(); _sliderTargets.Clear(); _sliderTimer?.Stop();
        _volumePopup?.Close(); _speedPopup?.Close();
        _picturePopup?.Close(); FlushModernPictureChanges();
        string folder = Player.ConfigFolder;
        string backup = Path.Combine(folder, "settings-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        foreach (string name in new[] { "mpv.conf", "frameflow.conf", "mpvnet.conf", "input.conf", "modern-ui.json", "settings.xml", "theme.conf" }) {
            string source = Path.Combine(folder, name);
            if (File.Exists(source)) File.Copy(source, Path.Combine(backup, name));
        }
        const string mpv = "osc=no\nborder=no\ntitle-bar=no\nkeepaspect-window=no\nhwdec=auto-safe\nsave-position-on-quit=no\nresume-playback=yes\nwatch-later-options=start\nkeep-open=yes\nautocreate-playlist=no\ninput-ipc-server=\\\\.\\pipe\\FrameFlow\n";
        const string app = "process-instance=single\nauto-load-folder=no\nstart-size=always\ndark-mode=always\n";
        File.WriteAllText(Player.ConfPath, mpv); File.WriteAllText(App.ConfPath, app);
        File.WriteAllText(Path.Combine(folder, "theme.conf"), "");
        string input = InputHelp.ConvertToString(InputHelp.GetDefaults());
        File.WriteAllText(App.InputConf.Path, input);
        App.InputConf.Content = input;
        foreach (string key in _shortcutBase.Keys.Concat(_shortcutAppliedKeys).Distinct().ToArray()) Player.CommandV("keybind", key, "ignore");
        _shortcutAppliedKeys.Clear(); Player.UsedInputConfContent = input;
        foreach (var binding in InputHelp.Parse(input))
            if (binding.Input.Length > 0) Player.CommandV("keybind", binding.Input, binding.Command.Length > 0 ? binding.Command : "ignore");
        var original = new AppSettings();
        foreach (var field in typeof(AppSettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
            if (field.Name != nameof(AppSettings.RecentFiles)) field.SetValue(App.Settings, field.GetValue(original));
        using (var stream = File.Create(Path.Combine(folder, "settings.xml"))) new XmlSerializer(typeof(AppSettings)).Serialize(stream, App.Settings);
        _modernSettings.RestoreDefaults();
        ApplyModernNightMode(); ModernPictureChanged?.Invoke();
        _navigationSuppressedUntil = Environment.TickCount64 + 600;
        ClearNavigationFeedback();
        if (_modernFullscreen) { Player.SetPropertyBool("fullscreen", false); SetModernFullscreen(false); }
        Player.SetPropertyBool("ontop", false);
        Player.SetPropertyDouble("speed", 1);
        Player.SetPropertyDouble("volume", original.Volume);
        Player.SetPropertyBool("mute", false);
        foreach (string property in new[] { "brightness", "contrast", "saturation", "gamma", "hue", "panscan", "sub-delay", "audio-delay" })
            Player.SetPropertyDouble(property, 0);
        Player.SetPropertyInt("video-rotate", 0);
        Player.SetPropertyDouble("sub-scale", 1);
        Player.SetPropertyBool("sub-visibility", true);
        ResetVideoView(false);
        InitializeModernShortcuts(); ApplyModernCursorMode(); ApplyModernVolumeOptions();
        _wheelBindingRemainder = _ctrlWheelBindingRemainder = 0; PublishModernMouseActions();
        ApplyPlaylistMode("sequence", false);
        SetPlaylistVisible(false); RebuildToolbar();
        // Keep automatic first-run dimensions rather than saving a forced size.
        _modernWindowTransition = true;
        try { WindowState = FormWindowState.Normal; Bounds = InitialModernWindowBounds(Screen.FromControl(this)); }
        finally { _modernWindowTransition = false; }
        _modernSettings.Width = _modernSettings.Height = _modernSettings.WindowDpi = 0;
        SaveModernSettings(); RefreshModernState(); PublishHeldNavigation(); PublishModernWindowState();
        Player.SetPropertyString("user-data/modern/defaults-reset", JsonSerializer.Serialize(new {
            backup, restored = true, recordsPreserved = true, nativeRestartRequired = true
        }));
        return backup;
    }
}
