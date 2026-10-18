using System.Globalization;
using System.Text.Json;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    SharedPlaybackProgress _sharedProgress = null!;
    string _resumeFile = "";
    string _resumeOwner = "";
    bool _resumeLoaded;
    bool _resumeAllowed;
    string _requestedStart = "none";
    double? _restoredPosition;
    string _resumeSource = "none";
    bool? _lastProgressSave;
    bool _resumeDefaultAllowed;
    bool _startupResumeRequest = true;
    readonly Dictionary<string, bool> _pendingResumeRequests = new();

    void InitializeSharedProgress()
    {
        _sharedProgress = new(_modernSettingsPath);
        _resumeDefaultAllowed = CommandLine.Contains("resume-playback") ? CommandLine.GetValue("resume-playback") != "no" :
            !Player.Conf.TryGetValue("resume-playback", out string? resume) || resume != "no";
        Player.FileLoadRequested += (path, options) => {
            bool allowed = options != null && options.TryGetValue("resume-playback", out string? choice)
                ? choice != "no" : !_startupResumeRequest || _resumeDefaultAllowed;
            lock (_resumePositionLock) {
                if (!_startupResumeRequest) _resumeDefaultAllowed = true;
                _pendingResumeRequests[SharedPlaybackProgress.FileKey(path)] = allowed;
            }
        };
        // Stop native watch_later restoration before it injects a start option.
        // Explicit start/seek requests still belong to libmpv; we never issue a
        // delayed seek from file-loaded or from the UI timer.
        Player.AddHook("on_load", 50, BeginSharedPlayback);
        Player.AddHook("on_preloaded", 50, RestoreModernPlaybackPosition);
        Player.AddHook("on_unload", 50, SaveCurrentPosition);
        Player.FileLoaded += () => {
            lock (_resumePositionLock) _resumeLoaded = true;
            PublishSharedProgress();
        };
        Player.Seek += ClaimCurrentProgress;
        Player.PlaybackRestart += SaveCurrentPosition;
        Player.ObservePropertyBool("pause", paused => { if (!paused) ClaimCurrentProgress(); });
    }

    void BeginSharedPlayback()
    {
        try { lock (_resumePositionLock) {
            _resumeFile = Player.GetPropertyString("path");
            _requestedStart = Player.GetPropertyString("start");
            _resumeAllowed = _pendingResumeRequests.Remove(SharedPlaybackProgress.FileKey(_resumeFile), out bool allowed)
                ? allowed : Player.GetPropertyBool("option-info/resume-playback/set-locally")
                    ? Player.GetPropertyBool("resume-playback") : _resumeDefaultAllowed;
            _resumeLoaded = false;
            _restoredPosition = null;
            _lastProgressSave = null;
            _resumeSource = _requestedStart == "none" ? "none" : "explicit-start";
            _resumeOwner = "";
            Player.SetPropertyString("file-local-options/resume-playback", "no");
            Player.SetPropertyString("file-local-options/save-position-on-quit", "no");
            if (!string.IsNullOrEmpty(_resumeFile)) _resumeOwner = _sharedProgress.Claim(_resumeFile);
        } } catch (Exception ex) { Terminal.WriteError("建立视频断点记录失败：" + ex.Message); }
        PublishSharedProgress();
    }

    void ClaimCurrentProgress()
    {
        try { lock (_resumePositionLock) {
            string path = Player.GetPropertyString("path");
            if (!_resumeLoaded || string.IsNullOrEmpty(path) || SharedPlaybackProgress.FileKey(path) != SharedPlaybackProgress.FileKey(_resumeFile)) return;
            _resumeOwner = _sharedProgress.Claim(path);
        } } catch (Exception ex) { Terminal.WriteError("更新视频断点记录失败：" + ex.Message); }
    }

    void SaveCurrentPosition()
    {
        try {
            lock (_resumePositionLock) {
                if (!_resumeLoaded || !_modernSettings.RememberPosition || string.IsNullOrEmpty(_resumeOwner)) return;
                string path = Player.GetPropertyString("path");
                if (string.IsNullOrEmpty(path) || SharedPlaybackProgress.FileKey(path) != SharedPlaybackProgress.FileKey(_resumeFile)) return;
                if (!double.TryParse(Player.GetPropertyString("time-pos"), NumberStyles.Float, CultureInfo.InvariantCulture, out double position) ||
                    !double.IsFinite(position) || position < 0) return;
                double duration = Player.GetPropertyDouble("duration");
                // A save during decoding/seeking would record the outgoing time,
                // not the completed request. playback-restart saves it instead.
                if (Player.GetPropertyBool("seeking") || Player.GetPropertyString("path") != path) return;
                _lastProgressSave = _sharedProgress.Save(path, _resumeOwner, duration > 0 && position >= duration - 2 ? 0 : position);
            }
            PublishSharedProgress();
        } catch (Exception ex) { Terminal.WriteError("保存视频断点失败：" + ex.Message); }
    }

    void RestoreModernPlaybackPosition()
    {
        try {
            lock (_resumePositionLock) {
                if (!_modernSettings.RememberPosition || !_resumeAllowed || _requestedStart != "none" || string.IsNullOrEmpty(_resumeFile)) return;
                double? position = _sharedProgress.Read(_resumeFile);
                _resumeSource = position.HasValue ? "shared-progress" : "none";
                if (!position.HasValue) {
                    string legacyDirectory = Player.GetPropertyString("watch-later-dir");
                    if (legacyDirectory.StartsWith("~~/")) legacyDirectory = Path.Combine(Player.ConfigFolder, legacyDirectory[3..]);
                    position = SharedPlaybackProgress.ReadLegacy(_resumeFile,
                        new[] { legacyDirectory, Path.Combine(Player.ConfigFolder, "watch_later") }.Where(x => !string.IsNullOrEmpty(x)));
                    if (position.HasValue) _resumeSource = "legacy-import";
                }
                // Checking the live start also protects a later-priority hook
                // that explicitly sets a per-file start during loading.
                if (position.HasValue && ResumePositionPolicy.CanRestore(true, true, Player.GetPropertyString("start"),
                    position.Value, Player.GetPropertyDouble("duration"))) {
                    Player.SetPropertyString("file-local-options/start", position.Value.ToString(CultureInfo.InvariantCulture));
                    _restoredPosition = position;
                }
            }
            PublishSharedProgress();
        } catch (Exception ex) { Terminal.WriteError("读取视频断点失败：" + ex.Message); }
    }

    void PublishSharedProgress()
    {
        if (Player.Handle == IntPtr.Zero) return;
        lock (_resumePositionLock)
            Player.SetPropertyString("user-data/modern/resume", JsonSerializer.Serialize(new {
                policy = "shared-latest-interaction", pid = Environment.ProcessId, file = _resumeFile,
                config = _modernSettingsPath, requestedStart = _requestedStart, source = _resumeSource,
                restored = _restoredPosition, loaded = _resumeLoaded, owner = _resumeOwner, saveAccepted = _lastProgressSave
            }));
    }
}
