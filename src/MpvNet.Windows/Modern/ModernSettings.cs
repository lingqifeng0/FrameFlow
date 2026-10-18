using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;

namespace MpvNet.Windows.Modern;

public sealed class ModernSettings
{
    public const int MaxToolbarItems = 16;
    // Preserve V16 configurations until the user removes their extra buttons.
    // The additional legacy items stay reachable through More, not new slots.
    public const int LegacyToolbarItemLimit = 32;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanAddToolbarItem => Toolbar.Count < MaxToolbarItems;
    [System.Text.Json.Serialization.JsonIgnore]
    public int RemainingToolbarSlots => Math.Max(0, MaxToolbarItems - Toolbar.Count);
    public bool TryAddToolbarItem(ToolbarItem item)
    {
        if (!CanAddToolbarItem) return false;
        Toolbar.Add(item);
        return true;
    }
    public const decimal MinimumSpeed = 0.25m;
    public const decimal MaximumSpeed = 16m;
    public bool RememberSpeed { get; set; } = true;
    public bool RememberPosition { get; set; } = true;
    public decimal Speed { get; set; } = 1;
    public decimal SpeedStep { get; set; } = 0.05m;
    public const decimal DefaultTemporarySpeed = 3m;
    public const decimal DefaultArrowSpeed = 6m;
    public const decimal DefaultTapMilliseconds = 500m;
    public decimal TemporaryFastForwardSpeed { get; set; } = DefaultTemporarySpeed;
    public decimal ArrowNavigationSpeed { get; set; } = DefaultArrowSpeed;
    public decimal ArrowDefaultSpeed { get; set; } = DefaultArrowSpeed;
    public decimal TapCalculationMilliseconds { get; set; } = DefaultTapMilliseconds;
    public decimal TapDefaultMilliseconds { get; set; } = DefaultTapMilliseconds;
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal TapSeekSeconds => ArrowNavigationSpeed * TapCalculationMilliseconds / 1000m;
    public bool ShowButtonLabels { get; set; }
    public bool AutoHideCursor { get; set; }
    public bool AutoHideFullscreenControls { get; set; } = true;
    public bool CtrlMouseVideoView { get; set; } = true;
    public string CtrlDragAction { get; set; } = "pan";
    public bool MouseWheelVolume { get; set; } = true;
    public string WheelAction { get; set; } = "volume";
    public string CtrlWheelAction { get; set; } = "zoom";
    public bool VolumeBoost { get; set; }
    public int VideoBrightness { get; set; }
    public bool NightMode { get; set; }
    public const int DefaultNightStrength = 35;
    public int NightStrength { get; set; } = DefaultNightStrength;
    // Older installations may already set brightness in mpv.conf. Adopt that
    // value once rather than replacing it when these controls first appear.
    internal bool HasSavedVideoBrightness { get; private set; }
    public Dictionary<string, string> ShortcutOverrides { get; set; } = new();
    public Dictionary<string, string> MouseActions { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public int VolumeMaximum => VolumeBoost ? 300 : 100;
    public bool ShowNavigationFeedback { get; set; } = true;
    public string VideoDoubleClickAction { get; set; } = DoubleClickActions.DefaultVideo;
    public string TitleBarDoubleClickAction { get; set; } = DoubleClickActions.DefaultTitleBar;
    public bool PlaylistVisible { get; set; }
    public bool AutoFolderPlaylist { get; set; } = true;
    public string PlaylistMode { get; set; } = "sequence";
    public int PlaylistWidth { get; set; } = 260;
    public List<ToolbarItem> Toolbar { get; set; } =
    [
        new("frame-back", "上一帧"), new("frame-forward", "下一帧"),
        new("screenshot", "截图"), new("bookmark", "书签"),
        new("ab-loop", "AB 循环"), new("timestamp", "复制时间戳")
    ];
    public Dictionary<string, double> Positions { get; set; } = [];
    public List<VideoBookmark> Bookmarks { get; set; } = [];
    // Zero means the first window uses a proportion of the monitor's work area.
    // Existing configurations still retain their explicitly saved dimensions.
    public int Width { get; set; }
    public int Height { get; set; }
    public int WindowDpi { get; set; }
    static readonly JsonSerializerOptions options = new() { WriteIndented = true };
    JsonObject? _baseline;

    public static ModernSettings Load(string path)
    {
        try
        {
            var result = SharedSettingsFile.Access(path, false, root => {
                var loaded = root.Deserialize<ModernSettings>(options) ?? new();
                loaded.HasSavedVideoBrightness = root.ContainsKey(nameof(VideoBrightness));
                if (!root.ContainsKey(nameof(WheelAction))) loaded.WheelAction = loaded.MouseWheelVolume ? "volume" : "none";
                if (!root.ContainsKey(nameof(CtrlWheelAction))) loaded.CtrlWheelAction = loaded.CtrlMouseVideoView ? "zoom" : "input-conf";
                if (!root.ContainsKey(nameof(CtrlDragAction))) loaded.CtrlDragAction = loaded.CtrlMouseVideoView ? "pan" : "input-conf";
                if (root.Count > 0 && !root.ContainsKey(nameof(ArrowNavigationSpeed))) {
                    // V21 used playback speed * 4. Capture its actual startup
                    // rate once; later normal-speed changes no longer affect it.
                    loaded.ArrowNavigationSpeed = NormalizeNavigationSpeed((loaded.RememberSpeed ? NormalizeSpeed(loaded.Speed) : 1m) * 4);
                    loaded.TapCalculationMilliseconds = Math.Round(120m / loaded.ArrowNavigationSpeed, 2);
                }
                return loaded;
            });
            result.Speed = NormalizeSpeed(result.Speed);
            result.VideoBrightness = Math.Clamp(result.VideoBrightness, -100, 100);
            result.NightStrength = NormalizeNightStrength(result.NightStrength);
            result.WheelAction = WheelActions.Normalize(result.WheelAction, false);
            result.CtrlWheelAction = WheelActions.Normalize(result.CtrlWheelAction, true);
            result.CtrlDragAction = MouseDragActions.Normalize(result.CtrlDragAction);
            result.CtrlMouseVideoView = result.CtrlDragAction == "pan";
            result.MouseWheelVolume = result.WheelAction == "volume"; // compatibility with older diagnostic/config readers
            result.TemporaryFastForwardSpeed = NormalizeNavigationSpeed(result.TemporaryFastForwardSpeed);
            result.ArrowNavigationSpeed = NormalizeNavigationSpeed(result.ArrowNavigationSpeed);
            // Factory restore targets belong to the current release, not to
            // a V21 migration or a previous version's saved default fields.
            result.ArrowDefaultSpeed = DefaultArrowSpeed;
            result.TapCalculationMilliseconds = NormalizeTapMilliseconds(result.TapCalculationMilliseconds);
            result.TapDefaultMilliseconds = DefaultTapMilliseconds;
            result.SpeedStep = result.SpeedStep is 0.01m or 0.05m or 0.1m ? result.SpeedStep : 0.05m;
            result.PlaylistWidth = Math.Clamp(result.PlaylistWidth, 210, 600);
            if (result.PlaylistMode is not ("sequence" or "single-loop" or "list-loop" or "random" or "stop-current")) result.PlaylistMode = "sequence";
            result.VideoDoubleClickAction = DoubleClickActions.Normalize(result.VideoDoubleClickAction, true);
            result.TitleBarDoubleClickAction = DoubleClickActions.Normalize(result.TitleBarDoubleClickAction, false);
            result.Toolbar = (result.Toolbar ?? []).Where(t => t != null && ToolbarCatalog.Find(t.Id) != null && double.IsFinite(t.Value)).Take(LegacyToolbarItemLimit).ToList();
            result.Positions ??= [];
            result.Bookmarks ??= [];
            result.ShortcutOverrides ??= new();
            result.MouseActions ??= new();
            result._baseline = JsonSerializer.SerializeToNode(result, options)!.AsObject();
            return result;
        }
        catch { return new(); }
    }
    public static decimal NormalizeSpeed(decimal speed) => Math.Round(Math.Clamp(speed, MinimumSpeed, MaximumSpeed), 2);
    public static int NormalizeNightStrength(int value) => (int)Math.Round(Math.Clamp(value, 0, 100) / 5d, MidpointRounding.AwayFromZero) * 5;
    public static decimal NormalizeNavigationSpeed(decimal speed) => Math.Round(Math.Clamp(speed, 1m, MaximumSpeed), 2);
    public static decimal NormalizeTapMilliseconds(decimal value) => Math.Round(Math.Clamp(value, 1m, 1000m), 2);

    public void RestoreDefaults()
    {
        var defaults = new ModernSettings();
        // Automatically include new preference properties. Resume positions and
        // bookmarks are records, and survive a preference reset.
        foreach (var property in typeof(ModernSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            if (property.CanWrite && property.Name is not (nameof(Positions) or nameof(Bookmarks)))
                property.SetValue(this, property.GetValue(defaults));
    }

    public void Save(string path)
    {
        ArrowDefaultSpeed = DefaultArrowSpeed;
        TapDefaultMilliseconds = DefaultTapMilliseconds;
        if (Bookmarks.Count > 1000) Bookmarks = Bookmarks.TakeLast(1000).ToList();
        var snapshot = JsonSerializer.SerializeToNode(this, options)!.AsObject();
        SharedSettingsFile.Access(path, true, latest => {
            foreach (var pair in snapshot) {
                // PlaybackProgress owns position writes. Saving UI preferences
                // must not restore the Positions snapshot loaded at startup.
                if (pair.Key == nameof(Positions) && latest.ContainsKey(nameof(Positions))) continue;
                if (pair.Key is nameof(ArrowDefaultSpeed) or nameof(TapDefaultMilliseconds) ||
                    !latest.ContainsKey(pair.Key) || _baseline == null || !JsonNode.DeepEquals(pair.Value, _baseline[pair.Key]))
                    latest[pair.Key] = pair.Value?.DeepClone();
            }
            return true;
        });
        _baseline = snapshot;
    }
}
public sealed record ToolbarItem(string Id, string Title, double Value = 0);
public sealed record VideoBookmark(string File, double Time, string Title);
