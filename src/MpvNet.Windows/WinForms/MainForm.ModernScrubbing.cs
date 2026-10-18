using System.Globalization;
using System.Windows.Forms;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    System.Windows.Forms.Timer? _seekPreviewTimer;
    System.Windows.Forms.Timer? _sliderTimer;
    double? _seekPreview, _seekCommit;
    long _seekCommitStarted;
    bool _scrubbing;
    int _seekPreviewCommands, _seekCommits;
    readonly Dictionary<string, double> _sliderProperties = new();
    readonly Dictionary<string, (double Value, long Started)> _sliderTargets = new();

    void InitializeModernScrubbing()
    {
        _seekPreviewTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _seekPreviewTimer.Tick += (_, _) => {
            if (_seekPreview is not { } time || !_modernSeek.IsDragging || _currentFile.Length == 0) return;
            _seekPreview = null;
            Player.CommandVAsync("seek", time.ToString(CultureInfo.InvariantCulture), "absolute+keyframes");
            _seekPreviewCommands++;
        };
        _modernSeek.SeekRequested += time => {
            if (_currentFile.Length == 0) return;
            if (!_scrubbing) {
                EndHeldNavigation(false);
                _scrubbing = true;
                _seekCommit = null;
                _navigationRequest = null;
                _navigationTimer?.Stop();
                AllowNavigationInput();
            }
            _modernTime.Text = TimeText(time);
            _seekPreview = time;
            _seekPreviewTimer.Start();
        };
        _modernSeek.SeekCompleted += time => {
            _seekPreviewTimer.Stop();
            _seekPreview = null;
            _scrubbing = false;
            if (_modernClosing || _currentFile.Length == 0) return;
            _seekCommit = time;
            _seekCommitStarted = Environment.TickCount64;
            if (_modernSettings.ShowNavigationFeedback) BeginNavigationFeedback("定位", _currentPosition);
            Player.CommandVAsync("seek", time.ToString(CultureInfo.InvariantCulture), "absolute+exact");
            _seekCommits++;
        };
    }

    void CancelModernScrubbing()
    {
        _seekPreviewTimer?.Stop();
        _seekPreview = _seekCommit = null;
        _scrubbing = false;
        _modernSeek.CancelDrag();
    }

    void UpdateModernSeekPosition(double position, double duration)
    {
        _modernSeek.Duration = double.IsFinite(duration) && duration > 0 ? duration : 0;
        if (_modernSeek.IsDragging) return;
        // Decoder state can still report the old position just after release.
        if (_seekCommit is { } target) {
            if (Math.Abs(position - target) > .5 && Environment.TickCount64 - _seekCommitStarted < 5000) return;
            _seekCommit = null;
        }
        _modernSeek.Position = position;
        _modernSeek.Invalidate();
    }

    void QueueSliderProperty(string name, double value)
    {
        if (!double.IsFinite(value)) return;
        if (name == "volume") value = Math.Clamp(value, 0, _modernSettings.VolumeMaximum);
        _sliderProperties[name] = value;
        _sliderTargets[name] = (value, Environment.TickCount64);
        _sliderTimer ??= CreateSliderTimer();
        _sliderTimer.Start();
    }

    System.Windows.Forms.Timer CreateSliderTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 33 };
        timer.Tick += (_, _) => FlushSliderProperties();
        return timer;
    }

    void FlushSliderProperties()
    {
        _sliderTimer?.Stop();
        if (Player.Handle == IntPtr.Zero || _modernClosing) { _sliderProperties.Clear(); return; }
        foreach (var (name, pending) in _sliderProperties) {
            double value = name == "volume" ? Math.Clamp(pending, 0, _modernSettings.VolumeMaximum) : pending;
            if (value != pending) _sliderTargets[name] = (value, Environment.TickCount64);
            Player.CommandVAsync("set", name, value.ToString(CultureInfo.InvariantCulture));
        }
        _sliderProperties.Clear();
    }

    double SliderDisplayValue(string name, double actual)
    {
        if (!_sliderTargets.TryGetValue(name, out var target)) return actual;
        if (Math.Abs(actual - target.Value) < .0001 || Environment.TickCount64 - target.Started >= 2000) {
            _sliderTargets.Remove(name);
            return actual;
        }
        return target.Value;
    }
}
