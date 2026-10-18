using System.Globalization;
using System.Text.Json;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    System.Windows.Forms.Timer? _heldNavigationTimer;
    int _heldDirection;
    bool _heldStarted, _heldWasPaused;
    double _heldSpeed, _heldOrigin, _heldLastTarget;
    double _heldRate;
    long _heldAt, _heldPlaybackAt;

    void HandleHeldNavigation(int direction, string state)
    {
        if (state.Length == 0 || _modernClosing) return;
        if (state[0] == 'u') { if (_heldDirection == direction) EndHeldNavigation(!state.Contains('c')); return; }
        if (state[0] == 'p') { if (direction != 2) StepHeldNavigation(direction); return; }
        if (state[0] != 'd' || _heldDirection == direction || _currentFile.Length == 0) return;
        EndHeldNavigation(false);
        CancelModernScrubbing();
        FlushSliderProperties();
        _heldWasPaused = Player.GetPropertyBool("pause");
        _heldSpeed = Player.GetPropertyDouble("speed");
        _heldRate = (double)ModernSettings.NormalizeNavigationSpeed(direction == 2 ?
            _modernSettings.TemporaryFastForwardSpeed : _modernSettings.ArrowNavigationSpeed);
        _heldOrigin = Player.GetPropertyDouble("time-pos");
        _heldLastTarget = _heldOrigin;
        _heldAt = Environment.TickCount64;
        _heldDirection = direction;
        _heldStarted = false;
        _heldNavigationTimer ??= CreateHeldNavigationTimer();
        _heldNavigationTimer.Start();
        PublishHeldNavigation();
    }

    System.Windows.Forms.Timer CreateHeldNavigationTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 40 };
        timer.Tick += (_, _) => {
            if (_heldDirection == 0 || _currentFile.Length == 0 || _modernClosing) { EndHeldNavigation(false); return; }
            long now = Environment.TickCount64;
            if (!_heldStarted) {
                if (now - _heldAt < 180) return;
                _heldStarted = true;
                _heldPlaybackAt = now;
                _heldOrigin = Player.GetPropertyDouble("time-pos");
                AllowNavigationInput();
                _navigationRequest = null;
                if (_heldDirection > 0) {
                    Player.SetPropertyDouble("speed", _heldRate);
                    Player.SetPropertyBool("pause", false);
                } else Player.SetPropertyBool("pause", true);
            }
            double position = Player.GetPropertyDouble("time-pos");
            double duration = Player.GetPropertyDouble("duration");
            if (_heldDirection < 0) {
                _heldLastTarget = Math.Clamp(_heldOrigin - (now - _heldPlaybackAt) / 1000d * _heldRate, 0, Math.Max(0, duration));
                // One latest preview at a time; decoding never builds a queue of
                // stale reverse seeks. Each frame can be displayed while paused.
                if (!Player.GetPropertyBool("seeking"))
                    Player.CommandVAsync("seek", _heldLastTarget.ToString(CultureInfo.InvariantCulture), "absolute+exact");
            }
            // Use the same decoded position for the thumb, clock and overlay.
            // A reverse target still awaiting decoding is not the visible frame.
            _modernSeek.Duration = duration;
            _modernSeek.Position = position;
            _modernSeek.Invalidate();
            _modernTime.Text = TimeText(position);
            _modernDuration.Text = TimeText(duration);
            if (NavigationFeedbackAllowed()) {
                string action = _heldDirection == 2 ? "临时快进" : _heldDirection > 0 ? "连续快进" : "连续快退";
                ShowNavigationFeedback(action, position, clearNativeOsd: _navigationAction != action);
            }
        };
        return timer;
    }

    void StepHeldNavigation(int direction)
    {
        NavigateVideo(direction > 0 ? "前进" : "后退", "seek", (direction * _modernSettings.TapSeekSeconds).ToString(CultureInfo.InvariantCulture), "relative+exact");
    }

    void EndHeldNavigation(bool complete)
    {
        if (_heldDirection == 0) return;
        _heldNavigationTimer?.Stop();
        int direction = _heldDirection;
        bool started = _heldStarted;
        // Keep the guard until synchronous speed/pause restoration completes.
        if (Player.Handle != IntPtr.Zero && !_modernClosing && started) {
            if (direction < 0 && complete) {
                if (NavigationFeedbackAllowed()) BeginNavigationFeedback("快退", Player.GetPropertyDouble("time-pos"));
                Player.CommandVAsync("seek", _heldLastTarget.ToString(CultureInfo.InvariantCulture), "absolute+exact");
            }
            Player.SetPropertyDouble("speed", _heldSpeed);
            Player.SetPropertyBool("pause", _heldWasPaused);
            _navigationLastPosition = Player.GetPropertyDouble("time-pos");
            _navigationLastPaused = _heldWasPaused;
            _navigationHasPosition = true;
        }
        _heldDirection = 0;
        _heldStarted = false;
        if (complete && !_modernClosing) {
            if (!started) { if (direction != 2) StepHeldNavigation(direction); }
            else if (direction > 0 && NavigationFeedbackAllowed())
                ShowNavigationFeedback("快进", Player.GetPropertyDouble("time-pos"));
            ShowFullscreenFooter();
        }
        PublishHeldNavigation();
    }

    void PublishHeldNavigation()
    {
        if (Player.Handle != IntPtr.Zero && !_modernClosing)
            Player.SetPropertyString("user-data/modern/held-navigation", JsonSerializer.Serialize(new {
                direction = _heldDirection, active = _heldDirection != 0, thresholdMilliseconds = 180,
                previewMilliseconds = 40, rate = _heldRate, originalSpeed = _heldSpeed, originalPaused = _heldWasPaused,
                source = _heldDirection == 2 ? "temporary-shortcut" : "arrows",
                temporarySpeed = _modernSettings.TemporaryFastForwardSpeed,
                arrowSpeed = _modernSettings.ArrowNavigationSpeed, tapMilliseconds = _modernSettings.TapCalculationMilliseconds,
                tapSeconds = _modernSettings.TapSeekSeconds
            }));
    }
}
