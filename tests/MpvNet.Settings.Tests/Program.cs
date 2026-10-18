using System.Text.Json;
using MpvNet.Windows.Modern;

string directory = Path.GetFullPath(args.Length > 0 ? args[0] : "test-results/settings-config");
Directory.CreateDirectory(directory);
string path = Path.Combine(directory, "modern-ui.json");
int checks = 0;
void Check(bool condition, string title)
{
    if (!condition) throw new Exception(title);
    checks++;
    Console.WriteLine("PASS: " + title);
}
ModernSettings Load(object value)
{
    File.WriteAllText(path, JsonSerializer.Serialize(value));
    return ModernSettings.Load(path);
}

var legacy = Load(new { Speed = 1.34m, PlaylistWidth = 354,
    Toolbar = new[] { new ToolbarItem("seek-forward", "前进 12.34 秒", 12.34) },
    Bookmarks = new[] { new VideoBookmark("local.mp4", 17.5, "示例") } });
Check(legacy.VideoDoubleClickAction == "pause" && legacy.TitleBarDoubleClickAction == "maximize",
    "missing video action defaults to play/pause; title-bar action defaults to maximize");
Check(legacy.Speed == 1.34m && legacy.PlaylistWidth == 354 && legacy.Toolbar.Single().Value == 12.34 && legacy.Bookmarks.Single().Time == 17.5,
    "migration retains speed, sidebar, custom toolbar and bookmarks");
Check(legacy.AutoHideFullscreenControls && legacy.CtrlMouseVideoView,
    "old configuration enables fullscreen auto-hide and Ctrl mouse view controls");
Check(legacy.ShowNavigationFeedback, "old configuration enables navigation feedback without resetting other preferences");
Check(legacy.MouseWheelVolume && !legacy.VolumeBoost && legacy.VolumeMaximum == 100,
    "legacy configuration keeps the volume wheel enabled and defaults to the 100% cap");
var volumeOptions = Load(new { MouseWheelVolume = false, VolumeBoost = true, Speed = 1.34m });
volumeOptions.Save(path);
var restoredVolume = ModernSettings.Load(path);
Check(!restoredVolume.MouseWheelVolume && restoredVolume.VolumeBoost && restoredVolume.VolumeMaximum == 300 && restoredVolume.Speed == 1.34m,
    "volume wheel switch and 300% extension survive save/reload independently of speed");
restoredVolume.VolumeBoost = false;
restoredVolume.Save(path);
Check(ModernSettings.Load(path).VolumeMaximum == 100 && !ModernSettings.Load(path).MouseWheelVolume,
    "disabling amplification restores 100% without enabling the volume wheel");
Check(restoredVolume.WheelAction=="none" && legacy.WheelAction=="volume" && legacy.CtrlWheelAction=="zoom",
    "old wheel switches migrate to explicit actions without enabling a disabled volume wheel");
foreach (var action in WheelActions.All) {
    var wheel=Load(new { WheelAction=action.Id, CtrlWheelAction=action.Id, CtrlMouseVideoView=false, Speed=1.34m });
    wheel.Save(path); var reopened=ModernSettings.Load(path);
    Check(reopened.WheelAction==action.Id && reopened.CtrlWheelAction==action.Id && !reopened.CtrlMouseVideoView && reopened.Speed==1.34m,
        action.Id+" wheel action persists independently from Ctrl drag and normal playback speed");
}
var invalidWheel=Load(new { WheelAction="unknown",CtrlWheelAction=(string?)null });
Check(invalidWheel.WheelAction=="volume" && invalidWheel.CtrlWheelAction=="zoom","invalid wheel choices independently fall back to their factory defaults");
Check(!File.ReadAllText(path).Contains("VolumeMaximum"), "derived volume maximum is not a second persisted preference");
var feedbackOff = Load(new { ShowNavigationFeedback = false, Speed = 1.34m });
feedbackOff.Save(path);
Check(!ModernSettings.Load(path).ShowNavigationFeedback && ModernSettings.Load(path).Speed == 1.34m,
    "disabled navigation feedback survives save/reload without changing speed");
foreach (decimal speed in new[] { 0.25m, 1.34m, 4.01m, 8m, 12.34m, 16m })
{
    var highSpeed = Load(new { Speed = speed, RememberSpeed = true, SpeedStep = 0.01m });
    highSpeed.Save(path);
    var restoredSpeed = ModernSettings.Load(path);
    Check(restoredSpeed.Speed == speed && restoredSpeed.SpeedStep == 0.01m && restoredSpeed.RememberSpeed,
        speed + "x and 0.01 step survive save/reload without the old 4x clamp");
}
Check(Load(new { Speed = 20m }).Speed == 16m && Load(new { Speed = 0m }).Speed == 0.25m,
    "out-of-range saved speeds safely clamp to the new bounds");
Check(Load(new { Speed = 12.345m }).Speed == 12.34m,
    "saved speed keeps the two-decimal UI precision");
var speedTool = ToolbarCatalog.Find("speed")!;
Check(speedTool.Parameterize(12.34).Value == 12.34 && speedTool.Parameterize(16).Value == 16,
    "custom toolbar buttons accept high speed values");
try { speedTool.Parameterize(16.01); throw new Exception("Out-of-range toolbar speed accepted"); }
catch (ArgumentOutOfRangeException) { Check(true, "toolbar rejects speeds above 16x"); }
var mouseFeaturesOff = Load(new { AutoHideFullscreenControls = false, CtrlMouseVideoView = false, Speed = 1.34m });
mouseFeaturesOff.Save(path);
var mouseFeaturesReopened = ModernSettings.Load(path);
Check(!mouseFeaturesReopened.AutoHideFullscreenControls && !mouseFeaturesReopened.CtrlMouseVideoView && mouseFeaturesReopened.Speed == 1.34m,
    "disabling either mouse feature survives restart without changing playback preferences");
Check(mouseFeaturesReopened.CtrlWheelAction=="input-conf","an old disabled Ctrl mouse feature retains its native wheel bindings during migration");
Check(mouseFeaturesReopened.CtrlDragAction=="input-conf","disabled V22 Ctrl dragging migrates without re-enabling it");
foreach(var action in MouseDragActions.All) {
    var drag=Load(new {CtrlDragAction=action.Id,CtrlWheelAction="volume",WheelAction="zoom"}); drag.Save(path);
    var reopened=ModernSettings.Load(path);
    Check(reopened.CtrlDragAction==action.Id && reopened.CtrlMouseVideoView==(action.Id=="pan") && reopened.CtrlWheelAction=="volume",
        action.Id+" Ctrl drag action survives restart independently of both wheel choices");
}

var invalid = Load(new { VideoDoubleClickAction = "unknown", TitleBarDoubleClickAction = "input-conf", Speed = 1.57m });
Check(invalid.VideoDoubleClickAction == "pause" && invalid.TitleBarDoubleClickAction == "maximize" && invalid.Speed == 1.57m,
    "invalid mouse actions fall back independently without discarding other settings");
var nulls = Load(new { VideoDoubleClickAction = (string?)null, TitleBarDoubleClickAction = (string?)null });
Check(nulls.VideoDoubleClickAction == "pause" && nulls.TitleBarDoubleClickAction == "maximize",
    "null mouse actions safely use defaults");

foreach (string action in new[] { "fullscreen", "maximize", "pause", "none", "input-conf" })
{
    var selected = Load(new { VideoDoubleClickAction = action, TitleBarDoubleClickAction = "pause", Speed = 1.34m });
    Check(selected.VideoDoubleClickAction == action && selected.TitleBarDoubleClickAction == "pause",
        action + " video action stays separate from title-bar pause");
    selected.Save(path);
    var reopened = ModernSettings.Load(path);
    Check(reopened.VideoDoubleClickAction == action && reopened.TitleBarDoubleClickAction == "pause" && reopened.Speed == 1.34m,
        action + " selection survives saving and reopening");
}
foreach (string action in new[] { "fullscreen", "maximize", "none" })
{
    var selected = Load(new { VideoDoubleClickAction = "pause", TitleBarDoubleClickAction = action });
    Check(selected.VideoDoubleClickAction == "pause" && selected.TitleBarDoubleClickAction == action,
        action + " title-bar action stays separate from video pause");
}
Check(new ModernSettings().Width == 0 && new ModernSettings().Height == 0 && new ModernSettings().AutoFolderPlaylist,
    "fresh configuration uses monitor-relative size and enables local folder discovery");
var windowSettings = Load(new { Width = 1333, Height = 777, WindowDpi = 144, PlaylistMode = "random", AutoFolderPlaylist = false });
windowSettings.Save(path);
var restoredWindow = ModernSettings.Load(path);
Check(restoredWindow.Width == 1333 && restoredWindow.Height == 777 && restoredWindow.WindowDpi == 144 &&
    restoredWindow.PlaylistMode == "random" && !restoredWindow.AutoFolderPlaylist, "window dimensions, DPI and playlist choices survive save/reload");
Check(Load(new { PlaylistMode = "invalid" }).PlaylistMode == "sequence", "unknown playback mode safely defaults to sequence");
var stopCurrent = Load(new { PlaylistMode = "stop-current", AutoFolderPlaylist = true, Positions = new Dictionary<string, double> { ["local.mp4"] = 83.5 } });
stopCurrent.Save(path);
Check(ModernSettings.Load(path).PlaylistMode == "stop-current" && ModernSettings.Load(path).AutoFolderPlaylist &&
    ModernSettings.Load(path).Positions["local.mp4"] == 83.5, "stop-current playback mode persists while retaining the folder queue and shared progress");
Check(ToolbarCatalog.Find("window-reset") != null, "initial-size restore can be added to the editable toolbar");
Check(ResumePositionPolicy.CanRestore(true, true, "none", 1037.29, 2191.66), "ordinary open retains custom resume");
Check(!ResumePositionPolicy.CanRestore(true, false, "none", 1037.29, 2191.66), "resume-playback=no disables custom resume too");
Check(!ResumePositionPolicy.CanRestore(false, true, "none", 1037.29, 2191.66), "remember-position switch is respected");
foreach (string explicitStart in new[] { "0", "461.850", "+20", "-30", "50%", "#2", "" })
    Check(!ResumePositionPolicy.CanRestore(true, true, explicitStart, 1037.29, 2191.66), "explicit or unavailable start takes precedence: " + explicitStart);
foreach (double invalidPosition in new[] { 0d, 1d, -3d, 2190d, double.NaN, double.PositiveInfinity })
    Check(!ResumePositionPolicy.CanRestore(true, true, "none", invalidPosition, 2191.66), "invalid or near-end resume is ignored: " + invalidPosition);

var originalBounds = new System.Drawing.Rectangle(100, 80, 1000, 700);
var minimumBounds = new System.Drawing.Size(520, 340);
System.Drawing.Rectangle[] resizedBounds = [new(180, 80, 920, 700), new(100, 80, 1080, 700),
    new(100, 130, 1000, 650), new(100, 80, 1000, 750), new(180, 130, 920, 650),
    new(100, 130, 1080, 650), new(180, 80, 920, 750), new(100, 80, 1080, 750)];
for (int edge = 0; edge < resizedBounds.Length; edge++)
    Check(WindowResizeBounds.Drag(originalBounds, edge, 80, 50, minimumBounds) == resizedBounds[edge], "resizing edge/corner " + edge + " preserves the opposite edge");
Check(WindowResizeBounds.Drag(originalBounds, 4, 900, 650, minimumBounds) == new System.Drawing.Rectangle(580, 440, 520, 340),
    "top-left minimum-size clamp keeps the bottom-right corner fixed");
Check(WindowResizeBounds.Drag(originalBounds, 7, -900, -650, minimumBounds) == new System.Drawing.Rectangle(100, 80, 520, 340),
    "bottom-right minimum-size clamp keeps the top-left corner fixed");

foreach (double scale in new[] { 1d, 1.25, 1.5, 2 }) {
    int IconWidth() => (int)Math.Round(40 * scale) + 2 * (int)Math.Round(2 * scale);
    var icons = Enumerable.Repeat(IconWidth(), 6).ToArray();
    Check(ToolbarLayout.Fit(icons.Sum(), icons, IconWidth()) == new ToolbarFit(6, false),
        $"{scale * 100}% DPI fits all icons without reserving an unnecessary More button");
    Check(ToolbarLayout.Fit(icons.Sum() - 1, icons, IconWidth()) == new ToolbarFit(4, true),
        $"{scale * 100}% DPI reserves the full More footprint at the overflow boundary");
    var mixed = Enumerable.Range(0, 32).Select(i => (int)Math.Round((100 + i % 4 * 25) * scale) +
        2 * (int)Math.Round(2 * scale)).ToArray();
    int previous = 0;
    for (int width = IconWidth(); width <= mixed.Sum(); width++) {
        var fit = ToolbarLayout.Fit(width, mixed, IconWidth());
        int used = mixed.Take(fit.VisibleCount).Sum() + (fit.Overflow ? IconWidth() : 0);
        if (used > width || fit.VisibleCount < previous || fit.Overflow != (fit.VisibleCount < mixed.Length))
            throw new Exception("Variable-width toolbar overlaps or changes order at " + width);
        previous = fit.VisibleCount;
    }
    Check(previous == mixed.Length, $"{scale * 100}% DPI keeps 32 variable-width buttons ordered and unclipped through every width");
}
Check(ToolbarLayout.Fit(0, Array.Empty<int>(), 44) == new ToolbarFit(0, false), "empty editable toolbar has no More button");
Check(ToolbarLayout.Fit(44, new[] { 150, 104, 125 }, 44) == new ToolbarFit(0, true), "narrow toolbar keeps all variable-width items reachable in More");
Check(ToolbarLayout.Fit(169, new[] { 125, 104 }, 44) == new ToolbarFit(1, true), "removing and reordering custom buttons recomputes the correct prefix");
var slots = new ModernSettings();
Check(slots.Toolbar.Count == 6 && slots.RemainingToolbarSlots == 10 && slots.CanAddToolbarItem,
    "V17 reserves 16 custom slots with six defaults and ten free slots");
foreach (var definition in ToolbarCatalog.All.Where(t => slots.Toolbar.All(item => item.Id != t.Id)).Take(10).ToArray())
    if (!slots.TryAddToolbarItem(definition.Item)) throw new Exception("A free custom slot was refused");
Check(slots.Toolbar.Count == 16 && slots.RemainingToolbarSlots == 0 && !slots.CanAddToolbarItem,
    "the six default buttons and ten additions fill exactly 16 slots");
var filledOrder = slots.Toolbar.ToArray();
Check(!slots.TryAddToolbarItem(ToolbarCatalog.Find("zoom-reset")!.Item) && slots.Toolbar.SequenceEqual(filledOrder),
    "a seventeenth addition is refused without changing any existing button or its order");
slots.Toolbar.RemoveAt(8);
Check(slots.RemainingToolbarSlots == 1 && slots.TryAddToolbarItem(ToolbarCatalog.Find("zoom-reset")!.Item) && slots.Toolbar.Count == 16,
    "removing a button frees one slot for a replacement");
slots.Save(path);
var restoredSlots = ModernSettings.Load(path);
Check(restoredSlots.Toolbar.SequenceEqual(slots.Toolbar) && !restoredSlots.CanAddToolbarItem,
    "the filled toolbar retains the replacement and ordering across save/reload");
Check(!File.ReadAllText(path).Contains("RemainingToolbarSlots") && !File.ReadAllText(path).Contains("CanAddToolbarItem"),
    "slot counters are derived rather than independent saved preferences");
var legacyButtons = ToolbarCatalog.All.Take(20).Select(t => t.Item).ToArray();
var legacySlots = Load(new { Toolbar = legacyButtons, Speed = 1.34m });
legacySlots.Save(path);
Check(ModernSettings.Load(path).Toolbar.SequenceEqual(legacyButtons) && !legacySlots.CanAddToolbarItem && legacySlots.RemainingToolbarSlots == 0,
    "V16 toolbars above 16 keep their existing buttons while new additions are disabled");
Check(ToolbarLayout.Fit(10000, Enumerable.Repeat(44, 20).ToArray(), 44, 16) == new ToolbarFit(16, true),
    "legacy extras stay in More even in a very wide V17 window");
foreach (int width in Enumerable.Range(44, 1000)) {
    var widths = Enumerable.Range(0, 20).Select(i => 44 + i % 3 * 10).ToArray();
    var fit = ToolbarLayout.Fit(width, widths, 44, 16);
    if (fit.VisibleCount > 16 || widths.Take(fit.VisibleCount).Sum() + (fit.Overflow ? 44 : 0) > width)
        throw new Exception("V17 toolbar exceeded its slot or width budget at " + width);
}
Check(true, "V17 preserves the ordered prefix and More width through 1000 narrow-to-wide layouts");
// Independent windows share one authoritative position dictionary; stale UI
// preference snapshots are not allowed to replace it or a different preference.
File.WriteAllText(path, "{\"Positions\":{\"video-a.mp4\":20},\"FutureField\":\"keep\"}");
var windowA = ModernSettings.Load(path);
var windowB = ModernSettings.Load(path);
var progressA = new SharedPlaybackProgress(path);
var progressB = new SharedPlaybackProgress(path);
string tokenA = progressA.Claim("video-a.mp4");
Check(progressA.Save("video-a.mp4", tokenA, 600.25), "owner can persist its actual video position");
string tokenB = progressB.Claim("video-a.mp4");
Check(progressB.Save("video-a.mp4", tokenB, 461.85), "new timestamp owns the common progress record");
Check(!progressA.Save("video-a.mp4", tokenA, 200) && progressA.Read("video-a.mp4") == 461.85,
    "old window cannot overwrite a newer timestamp even when exiting last");
string otherToken = progressA.Claim("video-b.mp4");
Check(progressA.Save("video-b.mp4", otherToken, 15) && progressA.Read("video-a.mp4") == 461.85,
    "a different video's save merges without replacing existing progress");
windowA.Speed = 1.34m;
windowA.Save(path);
windowB.Width = 1333;
windowB.Save(path);
var merged = ModernSettings.Load(path);
Check(merged.Speed == 1.34m && merged.Width == 1333 && progressA.Read("video-a.mp4") == 461.85,
    "two preference saves merge their changes and retain the latest resume position");
Check(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("FutureField").GetString() == "keep",
    "updates retain unfamiliar configuration fields");
Check(progressB.Save("video-a.mp4", tokenB, 0) && progressA.Read("video-a.mp4") == 0,
    "zero is a shared progress value, not a missing record to be replaced by legacy state");
tokenA = progressA.Claim("video-a.mp4");
Check(progressA.Save("video-a.mp4", tokenA, 77), "an intentional new interaction can reclaim an older window");
Check(progressA.Read("https://example.test/A.mp4") == null, "different files and URLs cannot borrow another video's progress");
if (OperatingSystem.IsWindows()) {
    string mediaKey = Path.GetFullPath("video-a.mp4");
    Check(progressA.Read(mediaKey.ToUpperInvariant().Replace('\\', '/')) == 77,
        "Windows path case and slash variants resolve to the same record");
    string variantOwner = progressB.Claim(mediaKey.ToUpperInvariant().Replace('\\', '/'));
    Check(!progressA.Save(mediaKey, tokenA, 88) && progressB.Save(mediaKey, variantOwner, 99),
        "path variants share ownership and cannot allow a stale window to write");
}
Parallel.For(0, 12, index => {
    string file = "parallel-" + index + ".mp4";
    string owner = progressA.Claim(file);
    if (!progressA.Save(file, owner, index + 10.5)) throw new Exception("Parallel owner was lost");
});
Check(Enumerable.Range(0, 12).All(index => progressA.Read("parallel-" + index + ".mp4") == index + 10.5),
    "concurrent saves keep every video's value in valid JSON");
string legacyDir = Path.Combine(directory, "legacy-cache");
Directory.CreateDirectory(legacyDir);
string legacyFile = Path.GetFullPath("legacy-video.mp4");
string legacyHash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(legacyFile)));
File.WriteAllText(Path.Combine(legacyDir, legacyHash), "start=777.7\n");
Check(SharedPlaybackProgress.ReadLegacy(legacyFile, new[] { legacyDir }) == 777.7,
    "legacy watch_later progress can be imported when no shared record exists");
var inputSettings = Load(new {
    ShortcutOverrides = new Dictionary<string,string> { ["pause"]="Ctrl+F9", ["mute"]="" },
    MouseActions = new Dictionary<string,string> { ["MBTN_Mid"]="cycle mute" }
});
inputSettings.Save(path);
var restoredInput = ModernSettings.Load(path);
Check(restoredInput.ShortcutOverrides.GetValueOrDefault("pause")=="Ctrl+F9" && restoredInput.ShortcutOverrides.ContainsKey("mute") &&
    restoredInput.ShortcutOverrides["mute"]=="" && restoredInput.MouseActions.GetValueOrDefault("MBTN_Mid")=="cycle mute",
    "recorded keys, explicit clearing and mouse mappings survive saving and reopening");
Check(Load(new { ShortcutOverrides=(object?)null, MouseActions=(object?)null }).ShortcutOverrides.Count==0,
    "legacy/null shortcut fields migrate to empty overrides");
Check(ShortcutCatalog.All.Select(item=>item.Id).Distinct().Count()==ShortcutCatalog.All.Count &&
    ShortcutCatalog.All.All(item=>item.Command!="seek 5" && item.Command!="seek -5"),
    "common shortcut identifiers are unique and exclude fixed five-second navigation");
Check(ShortcutCatalog.All.Count==26 && ShortcutCatalog.Find("seek-back-long")==null && ShortcutCatalog.Find("seek-forward-long")==null &&
    ShortcutCatalog.All.All(item=>item.Command!="seek 30" && item.Command!="seek -30"),
    "the 26 common shortcuts include picture controls without 30-second jumps");
var navigation = Load(new { Speed=1.25m });
Check(navigation.ArrowNavigationSpeed==5m && navigation.TapSeekSeconds==.12m && navigation.TemporaryFastForwardSpeed==3m,
    "V21 migration freezes its actual forward rate and keeps the original light-tap distance; independent shortcut defaults to 3x");
Check(navigation.ArrowDefaultSpeed==6m && navigation.TapDefaultMilliseconds==500m,
    "legacy navigation preserves current behavior but uses the new 6x/500ms factory restore targets");
navigation.Speed=2m;
Check(navigation.ArrowNavigationSpeed==5m && navigation.TapSeekSeconds==.12m,
    "later bottom-playback speed changes cannot alter either navigation setting");
navigation.ArrowNavigationSpeed=4m; navigation.TapCalculationMilliseconds=100m;
Check(navigation.TapSeekSeconds==.4m,"a 4x navigation rate and 100ms calculation produce a 0.4-second tap");
navigation.TemporaryFastForwardSpeed=8m; navigation.Save(path);
Check(ModernSettings.Load(path).TemporaryFastForwardSpeed==8m && ModernSettings.Load(path).TapSeekSeconds==.4m,
    "three navigation settings persist independently");
var damagedNavigation=Load(new { ArrowNavigationSpeed=40m, ArrowDefaultSpeed=-4m, TemporaryFastForwardSpeed=0m,
    TapCalculationMilliseconds=9000m, TapDefaultMilliseconds=-20m });
Check(damagedNavigation.ArrowNavigationSpeed==16m && damagedNavigation.TemporaryFastForwardSpeed==1m && damagedNavigation.TapCalculationMilliseconds==1000m,
    "corrupt navigation parameters are bounded independently");
var freshNavigation=Load(new {});
Check(freshNavigation.TemporaryFastForwardSpeed==3m && freshNavigation.ArrowNavigationSpeed==6m &&
    freshNavigation.TapCalculationMilliseconds==500m && freshNavigation.TapSeekSeconds==3m,
    "an empty new configuration uses 3x temporary, 6x arrows and 500ms calculation for a three-second tap");
var savedNavigation=Load(new { ArrowNavigationSpeed=8m, TapCalculationMilliseconds=125m, TemporaryFastForwardSpeed=12m,
    ArrowDefaultSpeed=5m, TapDefaultMilliseconds=24m });
Check(savedNavigation.ArrowNavigationSpeed==8m && savedNavigation.TapCalculationMilliseconds==125m && savedNavigation.TemporaryFastForwardSpeed==12m &&
    savedNavigation.ArrowDefaultSpeed==6m && savedNavigation.TapDefaultMilliseconds==500m,
    "upgrading the factory defaults preserves explicitly saved current values");
savedNavigation.Save(path);
using(var persistedDefaults=JsonDocument.Parse(File.ReadAllText(path)))
    Check(persistedDefaults.RootElement.GetProperty("ArrowDefaultSpeed").GetDecimal()==6m &&
        persistedDefaults.RootElement.GetProperty("TapDefaultMilliseconds").GetDecimal()==500m,
        "saving replaces the previous version's restore targets with canonical 6x/500ms values");
var pictureDefaults = Load(new {});
Check(pictureDefaults.VideoBrightness==0 && !pictureDefaults.NightMode && pictureDefaults.NightStrength==35 && !pictureDefaults.HasSavedVideoBrightness,
    "new picture controls default to original brightness, night off and 35% warmth without hiding native legacy brightness");
var picture = Load(new { VideoBrightness=-27, NightMode=true, NightStrength=65, Speed=1.34m });
picture.Save(path);
var reopenedPicture = ModernSettings.Load(path);
Check(reopenedPicture.VideoBrightness==-27 && reopenedPicture.NightMode && reopenedPicture.NightStrength==65 && reopenedPicture.HasSavedVideoBrightness && reopenedPicture.Speed==1.34m,
    "brightness and night strength survive reopening independently from normal playback speed");
var invalidPicture = Load(new { VideoBrightness=500, NightStrength=-5 });
Check(invalidPicture.VideoBrightness==100 && invalidPicture.NightStrength==0 && !invalidPicture.NightMode,
    "corrupt picture preferences are bounded without enabling night mode");
Check(Load(new { VideoBrightness=-500, NightStrength=999 }).VideoBrightness==-100 &&
    Load(new { NightStrength=999 }).NightStrength==100 && ModernSettings.NormalizeNightStrength(37)==35,
    "brightness lower bound and the reusable five-percent warmth levels normalize correctly");
Check(ToolbarCatalog.Find("picture")!=null && ToolbarCatalog.Find("night-mode")!=null && ShortcutCatalog.Find("night-mode")!=null,
    "picture and night controls can be assigned through the editable toolbar and common shortcut list");
var reset=Load(new { Speed=2m, VolumeBoost=true, MouseWheelVolume=false, Width=1234,
    VideoBrightness=37, NightMode=true, NightStrength=90,
    ArrowNavigationSpeed=12m, TemporaryFastForwardSpeed=10m, TapCalculationMilliseconds=100m,
    ShortcutOverrides=new Dictionary<string,string>{{"pause","Ctrl+F9"}},
    Positions=new Dictionary<string,double>{{"saved.mp4",77}}, Bookmarks=new[]{new VideoBookmark("saved.mp4",44,"keep")} });
reset.RestoreDefaults();
var defaults=new ModernSettings();
foreach(var property in typeof(ModernSettings).GetProperties().Where(property=>property.CanWrite && property.Name is not (nameof(ModernSettings.Positions) or nameof(ModernSettings.Bookmarks))))
    if (JsonSerializer.Serialize(property.GetValue(reset))!=JsonSerializer.Serialize(property.GetValue(defaults))) throw new Exception("Global reset missed "+property.Name);
Check(true,"every public preference participates in the global reset, including new navigation settings, shortcuts and toolbar");
Check(reset.Positions["saved.mp4"]==77 && reset.Bookmarks.Single().Title=="keep","global preference reset preserves positions and bookmarks");
Check(reset.VideoBrightness==0 && !reset.NightMode && reset.NightStrength==35,
    "restore-all includes original video brightness, night mode off and 35% warmth");
reset.Save(path);
Check(ModernSettings.Load(path).ArrowNavigationSpeed==6m && ModernSettings.Load(path).TapCalculationMilliseconds==500m && ModernSettings.Load(path).TapSeekSeconds==3m,
    "restored factory navigation defaults survive reopening");
Console.WriteLine($"All {checks} checks passed.");
