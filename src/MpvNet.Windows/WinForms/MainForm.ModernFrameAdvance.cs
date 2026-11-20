using System.Windows.Forms;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    enum FrameStepDirection { Forward, Backward }

    sealed class PendingFrameStep(FrameStepDirection direction, double origin)
    {
        public FrameStepDirection Direction { get; } = direction;
        public double Origin { get; } = origin;
        public long RequestedAt { get; } = Environment.TickCount64;
        public bool Restarted { get; set; }
    }

    const int FrameHoldDelay = 300;
    const double FramePositionTolerance = .00001;
    System.Windows.Forms.Timer? _frameAdvanceTimer;
    ModernButton? _frameAdvanceButton;
    string? _frameAdvanceKey;
    FrameStepDirection _frameAdvanceDirection;
    long _frameAdvanceDue;
    PendingFrameStep? _pendingFrameStep;
    (string KeepOpen, string KeepOpenPause, string LoopFile)? _frameStepEndOptions;

    bool FrameAdvanceHeld => _frameAdvanceButton != null || _frameAdvanceKey != null;

    void EnableFrameAdvanceHold(ModernButton button, FrameStepDirection direction)
    {
        button.LeftPressStarted += () => BeginFrameAdvance(direction, button, null);
        button.LeftPressEnded += () => {
            if (_frameAdvanceButton == button) EndFrameAdvanceHold(cancelClick: false);
        };
        button.MouseUp += (_, e) => {
            if (e.Button == MouseButtons.Left) RestorePlaybackKeyFocus(button);
        };
        button.MouseCaptureChanged += (_, _) => {
            if (!button.Capture && _frameAdvanceButton == button) EndFrameAdvanceHold();
        };
        button.VisibleChanged += (_, _) => {
            if (!button.Visible && _frameAdvanceButton == button) EndFrameAdvanceHold();
        };
        button.EnabledChanged += (_, _) => {
            if (!button.Enabled && _frameAdvanceButton == button) EndFrameAdvanceHold();
        };
        button.Disposed += (_, _) => {
            if (_frameAdvanceButton == button) EndFrameAdvanceHold();
        };
        _modernTips.SetToolTip(button, FrameStepTitle(direction) + " · 按住连续逐帧");
    }

    void HandleFrameAdvanceKey(FrameStepDirection direction, string state, string key)
    {
        if (state.Length == 0) return;
        switch (state[0]) {
            case 'd' when _frameAdvanceKey != key || _frameAdvanceDirection != direction:
                BeginFrameAdvance(direction, null, key);
                AdvanceOneFrame(direction);
                break;
            case 'u' when _frameAdvanceKey == key && _frameAdvanceDirection == direction:
                EndFrameAdvanceHold();
                break;
            case 'p':
                StepFrameOnce(direction);
                break;
            // The same timer drives a held key and a held mouse button.
            // Ignore all OS and mpv repeat messages.
        }
    }

    void BeginFrameAdvance(FrameStepDirection direction, ModernButton? button, string? key)
    {
        EndFrameAdvanceHold();
        if (!CanAdvanceFrame()) return;
        _frameAdvanceButton = button;
        _frameAdvanceKey = key;
        _frameAdvanceDirection = direction;
        _frameAdvanceDue = Environment.TickCount64 + FrameHoldDelay;
        StartFrameAdvanceTimer();
    }

    void StepFrameOnce(FrameStepDirection direction)
    {
        EndFrameAdvanceHold();
        AdvanceOneFrame(direction);
    }

    bool CanAdvanceFrame() => !_modernClosing && Player.Handle != IntPtr.Zero &&
        _currentFile.Length > 0 && Player.GetPropertyInt("video-params/w") > 0;

    static string FrameStepTitle(FrameStepDirection direction) => direction == FrameStepDirection.Backward ? "上一帧" : "下一帧";

    void StopAtFrameBoundary(FrameStepDirection direction, double position)
    {
        EndFrameAdvanceHold();
        if (!NavigationFeedbackAllowed()) return;
        _navigationRequest = null;
        _navigationLastPosition = position;
        _navigationLastPaused = true;
        _navigationHasPosition = true;
        string boundary = direction == FrameStepDirection.Backward ? " · 已到开头" : " · 已到末尾";
        ShowNavigationFeedback(FrameStepTitle(direction), position, boundary);
    }

    void AdvanceOneFrame(FrameStepDirection direction)
    {
        if (!CanAdvanceFrame()) {
            if (!_modernClosing) ShowToast("请先打开有画面的视频");
            EndFrameAdvanceHold();
            return;
        }
        EndHeldNavigation(false);
        CancelModernScrubbing();
        AllowNavigationInput();
        bool backwards = direction == FrameStepDirection.Backward;
        if (backwards && !Player.GetPropertyBool("pause")) Player.SetPropertyBool("pause", true);
        double position = Player.GetPropertyDouble("time-pos");
        if (backwards ? position <= FramePositionTolerance : Player.GetPropertyBool("eof-reached")) {
            StopAtFrameBoundary(direction, position);
            return;
        }
        _pendingFrameStep = new(direction, position);
        if (!backwards) KeepFrameAtEndOfFile();
        ExecuteVideoNavigation(FrameStepTitle(direction), backwards ? ["frame-back-step"] : ["frame-step"]);
        StartFrameAdvanceTimer();
        ShowFullscreenFooter();
    }

    void StartFrameAdvanceTimer()
    {
        if (_frameAdvanceTimer == null) {
            _frameAdvanceTimer = new System.Windows.Forms.Timer { Interval = 20 };
            _frameAdvanceTimer.Tick += (_, _) => TickFrameAdvance();
        }
        _frameAdvanceTimer.Start();
    }

    void TickFrameAdvance()
    {
        if (_modernClosing || Player.Handle == IntPtr.Zero || _currentFile.Length == 0) {
            CancelFrameAdvance();
            return;
        }
        long now = Environment.TickCount64;
        if (_pendingFrameStep is { } pending && !CompleteFrameStep(pending)) {
            if (now - pending.RequestedAt >= 5000) CancelFrameAdvance();
            return;
        }
        if (!FrameAdvanceHeld) {
            _frameAdvanceTimer?.Stop();
            return;
        }
        if (now < _frameAdvanceDue || Player.GetPropertyBool("seeking")) return;
        _frameAdvanceButton?.SuppressLeftClick();
        long interval = (long)Math.Round(1000d / _modernSettings.FrameStepsPerSecond);
        // Correct normal timer jitter without catching up on steps missed during decoding.
        _frameAdvanceDue = (now - _frameAdvanceDue > interval / 2 ? now : _frameAdvanceDue) + interval;
        AdvanceOneFrame(_frameAdvanceDirection);
    }

    bool CompleteFrameStep(PendingFrameStep pending)
    {
        // A forward step briefly unpauses. A backward step queues an exact
        // seek, so pause alone cannot tell whether the earlier frame is ready.
        if (Player.GetPropertyBool("seeking") || !Player.GetPropertyBool("pause")) return false;
        double position = Player.GetPropertyDouble("time-pos");
        bool backwards = pending.Direction == FrameStepDirection.Backward;
        bool movedBack = position < pending.Origin - FramePositionTolerance;
        if (backwards && !movedBack && !pending.Restarted) return false;

        _pendingFrameStep = null;
        RestoreFrameStepEndOptions();
        if (double.IsFinite(position) && position >= 0) {
            _currentPosition = position;
            UpdateModernSeekPosition(position, Player.GetPropertyDouble("duration"));
            _modernTime.Text = TimeText(_modernSeek.Position);
            if (backwards && (!FrameAdvanceHeld || _frameAdvanceDirection == pending.Direction)) {
                if (position <= FramePositionTolerance) StopAtFrameBoundary(pending.Direction, position);
                else if (!movedBack) EndFrameAdvanceHold();
            }
        }
        return true;
    }

    void EndFrameAdvanceHold(bool cancelClick = true)
    {
        if (cancelClick) _frameAdvanceButton?.SuppressLeftClick();
        _frameAdvanceButton = null;
        _frameAdvanceKey = null;
        if (_pendingFrameStep == null) _frameAdvanceTimer?.Stop();
    }

    void CancelFrameAdvance()
    {
        _pendingFrameStep = null;
        RestoreFrameStepEndOptions();
        EndFrameAdvanceHold();
    }

    void KeepFrameAtEndOfFile()
    {
        if (_frameStepEndOptions != null) return;
        string keepOpen = Player.GetPropertyString("keep-open");
        string keepOpenPause = Player.GetPropertyString("keep-open-pause");
        string loopFile = Player.GetPropertyString("loop-file");
        _frameStepEndOptions = (keepOpen, keepOpenPause, loopFile);
        // A native step can reach EOF while briefly unpaused. Keep the last
        // decoded frame in this file until the step has settled.
        if (keepOpen != "always") Player.SetPropertyString("keep-open", "always");
        if (keepOpenPause == "no") Player.SetPropertyString("keep-open-pause", "yes");
        if (loopFile is not ("no" or "")) Player.SetPropertyString("loop-file", "no");
    }

    void RestoreFrameStepEndOptions()
    {
        if (_frameStepEndOptions is not { } options) return;
        _frameStepEndOptions = null;
        if (Player.Handle == IntPtr.Zero) return;
        Restore("keep-open", "always", options.KeepOpen);
        Restore("keep-open-pause", "yes", options.KeepOpenPause);
        Restore("loop-file", "no", options.LoopFile);

        void Restore(string property, string stepValue, string original)
        {
            if (original.Length > 0 && original != stepValue && Player.GetPropertyString(property) == stepValue)
                Player.SetPropertyString(property, original);
        }
    }
}
