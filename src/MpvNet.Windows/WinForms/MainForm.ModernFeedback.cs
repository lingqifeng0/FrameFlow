using System.Drawing;
using System.Globalization;
using System.Text.Json;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    // Each mpv client owns its overlay IDs. This overlay keeps native OSD styles,
    // subtitles and other scripts' overlays intact and works over paused video.
    const string NavigationOverlayId = "901";
    const int NavigationDuration = 3000;
    System.Windows.Forms.Timer? _navigationTimer;
    NavigationRequest? _navigationRequest;
    long _navigationSuppressedUntil;
    long _navigationExpires;
    double _navigationLastPosition;
    bool _navigationLastPaused;
    bool _navigationHasPosition;
    string _navigationText = "";
    string _navigationAction = "";
    double _navigationPosition;
    string _navigationRendered = "";
    string _feedbackKind = "navigation";

    sealed class NavigationRequest(string action, double source, int frameDirection = 0, bool nativeSeek = false)
    {
        public string Action { get; } = action;
        public double Source { get; } = source;
        public int FrameDirection { get; } = frameDirection;
        public bool NativeSeek { get; } = nativeSeek;
        public long Started { get; } = Environment.TickCount64;
        public bool SeekObserved { get; set; }
        public bool Restarted { get; set; }
    }

    void InitializeNavigationFeedback()
    {
        _navigationTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _navigationTimer.Tick += (_, _) => UpdateNavigationFeedback();
        Player.Seek += () => Ui(() => {
            if (_modernSeek.IsDragging || _heldDirection != 0) return;
            if (!NavigationFeedbackAllowed()) return;
            if (_navigationRequest == null)
                BeginNavigationFeedback("定位", _navigationLastPosition, nativeSeek: true);
            if (_navigationRequest != null) _navigationRequest.SeekObserved = true;
        });
        Player.PlaybackRestart += () => Ui(() => {
            if (_navigationRequest != null) _navigationRequest.Restarted = true;
        });
        Player.ObservePropertyDouble("time-pos", position => Ui(() => ObserveNavigationPosition(position)));
        _videoSurface.Resize += (_, _) => RenderNavigationFeedback();
        InitializeOperationFeedback();
    }

    bool NavigationFeedbackAllowed() => !_modernClosing && _modernSettings.ShowNavigationFeedback &&
        Environment.TickCount64 >= _navigationSuppressedUntil && _currentFile.Length > 0;

    void AllowNavigationInput() => _navigationSuppressedUntil = 0;

    void NavigateVideo(string action, params string[] command)
    {
        EndHeldNavigation(false);
        CancelModernScrubbing();
        if (_currentFile.Length == 0) { ShowToast("请先打开视频"); return; }
        AllowNavigationInput();
        if (_modernSettings.ShowNavigationFeedback)
            BeginNavigationFeedback(action, Player.GetPropertyDouble("time-pos"),
                command[0] == "frame-back-step" ? -1 : command[0] == "frame-step" ? 1 : 0);
        Player.CommandV(command);
    }

    void BeginNavigationFeedback(string action, double source, int frameDirection = 0, bool nativeSeek = false)
    {
        _navigationRequest = new NavigationRequest(action, source, frameDirection, nativeSeek);
        _navigationTimer?.Start();
    }

    void ObserveNavigationPosition(double position)
    {
        if (!double.IsFinite(position) || position < 0) return;
        bool paused = Player.Paused;
        // A time-pos notification may have been queued before playback paused.
        // Do not infer a frame step from that stale position and then its newer
        // replacement. Real paused frame steps still match the current core.
        if (paused && Math.Abs(position - Player.GetPropertyDouble("time-pos")) > .00001) return;
        // Native frame shortcuts and external frame-step commands need feedback,
        // too. Playback itself never creates a prompt. Native seeks are handled
        // by Seek/PlaybackRestart; a small paused change covers frame-step, which
        // does not always emit a seek event.
        double delta = position - _navigationLastPosition;
        double fps = Player.GetPropertyDouble("container-fps");
        double frameWindow = fps > 0 ? Math.Clamp(2 / fps, .05, .5) : .25;
        if (_heldDirection == 0 && _navigationHasPosition && _navigationLastPaused && paused &&
            _navigationRequest == null && Math.Abs(delta) > .00001 && Math.Abs(delta) <= frameWindow &&
            NavigationFeedbackAllowed()) {
            BeginNavigationFeedback(delta < 0 ? "上一帧" : "下一帧", _navigationLastPosition, Math.Sign(delta));
        }
        _navigationLastPosition = position;
        _navigationLastPaused = paused;
        _navigationHasPosition = true;
    }

    void UpdateNavigationFeedback()
    {
        if (_modernClosing || Player.Handle == IntPtr.Zero) { _navigationTimer?.Stop(); return; }
        if (!_modernSettings.ShowNavigationFeedback && _feedbackKind == "navigation") { ClearNavigationFeedback(); return; }
        var pending = _navigationRequest;
        if (pending != null) {
            long age = Environment.TickCount64 - pending.Started;
            double position = Player.GetPropertyDouble("time-pos");
            double duration = Player.GetPropertyDouble("duration");
            double fps = Player.GetPropertyDouble("container-fps");
            double frame = fps > 0 ? 1 / fps : .04;
            bool changed = pending.FrameDirection switch {
                -1 => position < pending.Source - .00001,
                1 => position > pending.Source + .00001,
                _ => Math.Abs(position - pending.Source) > .00001
            };
            bool atBoundary = pending.FrameDirection < 0 && pending.Source <= .001 ||
                pending.FrameDirection > 0 && duration > 0 && pending.Source >= duration - frame * 1.5;
            if (double.IsFinite(position) && !Player.GetPropertyBool("seeking") &&
                (changed || pending.Restarted || age >= 180 && (atBoundary || pending.SeekObserved) || age >= 10000)) {
                _navigationRequest = null;
                string boundary = !changed && atBoundary ? pending.FrameDirection < 0 ? " · 已到开头" : " · 已到末尾" : "";
                string action = pending.Action;
                if (pending.NativeSeek && Player.Paused && changed && Math.Abs(position - pending.Source) <= frame * 1.5)
                    action = position < pending.Source ? "上一帧" : "下一帧";
                ShowNavigationFeedback(action, position, boundary);
            }
            else if (age >= 150 && _navigationAction != pending.Action + "-pending")
                ShowNavigationFeedback(pending.Action + "-pending", position, "", pending.Action + " · 正在定位…");
        }
        if (_navigationText.Length > 0) {
            if (_navigationRequest == null && (!_heldStarted || _feedbackKind != "navigation") &&
                Environment.TickCount64 >= _navigationExpires) ClearNavigationFeedback();
            else RenderNavigationFeedback();
        }
        else if (_navigationRequest == null) _navigationTimer?.Stop();
    }

    void ShowNavigationFeedback(string action, double position, string suffix = "", string? text = null, bool clearNativeOsd = true)
    {
        _feedbackKind = "navigation";
        _navigationAction = action;
        _navigationPosition = Math.Max(0, double.IsFinite(position) ? position : 0);
        double duration = Player.GetPropertyDouble("duration");
        string percent = duration > 0 ? $"（{Math.Clamp(position / duration * 100, 0, 100):0.#}%）" : "";
        _navigationText = text ?? $"{action}：{NavigationTimeText(position)}{percent}{suffix}";
        _navigationExpires = Environment.TickCount64 + NavigationDuration;
        // Navigation takes precedence over an older transient filename/seek
        // message; avoid two texts occupying the same top-left corner.
        if (clearNativeOsd) Player.CommandV("show-text", "", "1");
        RenderNavigationFeedback();
        PublishNavigationFeedback(true);
        _navigationTimer?.Start();
    }

    static string NavigationTimeText(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    void RenderNavigationFeedback()
    {
        if (_navigationText.Length == 0 || Player.Handle == IntPtr.Zero || _modernClosing) return;
        Size size = _videoSurface.ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        double scale = DeviceDpi / 96d;
        int x = (int)Math.Round(16 * scale);
        int y = (_modernFullscreen ? _modernHeader.Height : 0) + (int)Math.Round(12 * scale);
        int font = (int)Math.Round(20 * scale);
        string safeText = _navigationText.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\n", "\\N");
        string ass = $"{{\\an7\\pos({x},{y})\\fnMicrosoft YaHei UI\\fs{font}\\1c&HFFA76F&\\3c&H000000&\\bord1.5\\shad1\\q2}}" + safeText;
        string rendered = size + ass;
        if (rendered == _navigationRendered) return;
        Player.CommandV("osd-overlay", NavigationOverlayId, "ass-events", ass,
            size.Width.ToString(CultureInfo.InvariantCulture), size.Height.ToString(CultureInfo.InvariantCulture), "10");
        _navigationRendered = rendered;
    }

    void PublishNavigationFeedback(bool visible)
    {
        if (Player.Handle == IntPtr.Zero) return;
        double duration = Player.GetPropertyDouble("duration");
        Player.SetPropertyString("user-data/modern/navigation", JsonSerializer.Serialize(new {
            visible, kind = _feedbackKind, action = _navigationAction, time = _navigationPosition,
            timestamp = NavigationTimeText(_navigationPosition),
            percent = duration > 0 ? Math.Clamp(_navigationPosition / duration * 100, 0, 100) : 0,
            text = _navigationText, durationSeconds = 3
        }));
    }

    void ClearNavigationFeedback()
    {
        _navigationRequest = null;
        _navigationTimer?.Stop();
        // data is a required command argument even for format=none.
        if (_navigationRendered.Length > 0 && Player.Handle != IntPtr.Zero)
            Player.CommandV("osd-overlay", NavigationOverlayId, "none", "");
        _navigationRendered = "";
        _navigationText = "";
        PublishNavigationFeedback(false);
    }

    void ShowOperationFeedback(string action, string text)
    {
        _navigationRequest = null;
        ShowNavigationFeedback(action, Player.GetPropertyDouble("time-pos"), text: text);
        _feedbackKind = "operation";
        PublishNavigationFeedback(true);
    }

    void InitializeOperationFeedback()
    {
        // Replace the central native bar/text with one timed top-left overlay.
        Player.SetPropertyBool("osd-bar", false);
        Player.SetPropertyInt("osd-level", 0);
        void Watch(string property, Func<string> text)
        {
            bool initialized = false;
            string previous = "";
            Player.ObservePropertyString(property, value => Ui(() => {
                // Temporary held-navigation speed/pause must not replace its
                // timestamp, including notifications queued until after key-up.
                // Keep the original baseline so restoration is not a new action.
                if (property is "speed" or "pause" &&
                    (_heldDirection != 0 || value != Player.GetPropertyString(property))) return;
                bool changed = initialized && previous != value;
                initialized = true;
                previous = value;
                if (property is "volume" or "mute") UpdateVolumePopup();
                if (changed && !(property == "pause" && _navigationRequest != null) && !_modernClosing && _currentFile.Length > 0 && Environment.TickCount64 >= _navigationSuppressedUntil)
                    ShowOperationFeedback(property, text());
            }));
        }
        Watch("volume", () => $"音量：{Player.GetPropertyDouble("volume"):0.#}%" + (Player.GetPropertyBool("mute") ? "（静音）" : ""));
        Watch("mute", () => Player.GetPropertyBool("mute") ? "已静音" : $"恢复声音 · 音量：{Player.GetPropertyDouble("volume"):0.#}%");
        Watch("speed", () => $"播放速度：{Player.GetPropertyDouble("speed"):0.00} ×");
        Watch("pause", () => Player.Paused ? "暂停" : "播放");
        Watch("fullscreen", () => Player.GetPropertyBool("fullscreen") ? "进入全屏" : "退出全屏");
        Watch("ontop", () => Player.GetPropertyBool("ontop") ? "窗口置顶：开启" : "窗口置顶：关闭");
        Watch("sub-visibility", () => Player.GetPropertyBool("sub-visibility") ? "字幕：显示" : "字幕：隐藏");
        Watch("sid", () => "字幕：" + Player.GetPropertyString("sid"));
        Watch("aid", () => "音轨：" + Player.GetPropertyString("aid"));
        Watch("sub-delay", () => $"字幕延迟：{Player.GetPropertyDouble("sub-delay"):0.00} 秒");
        Watch("audio-delay", () => $"声音延迟：{Player.GetPropertyDouble("audio-delay"):0.00} 秒");
        Watch("sub-scale", () => $"字幕大小：{Player.GetPropertyDouble("sub-scale"):0.00} ×");
        Watch("video-zoom", () => $"画面缩放：{Math.Pow(2, Player.GetPropertyDouble("video-zoom")):0%}");
        Watch("video-rotate", () => $"画面旋转：{Player.GetPropertyInt("video-rotate")}°");
        foreach (string property in new[] { "brightness", "contrast", "saturation" }) {
            string title = property == "brightness" ? "亮度" : property == "contrast" ? "对比度" : "饱和度";
            Watch(property, () => $"{title}：{Player.GetPropertyDouble(property):0.#}");
        }
    }

    void ResetNavigationFeedback()
    {
        ClearNavigationFeedback();
        _navigationHasPosition = false;
        _navigationSuppressedUntil = Environment.TickCount64 + 800;
    }
}
