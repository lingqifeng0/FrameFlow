namespace MpvNet.Windows.Modern;

public sealed record ShortcutAction(string Id, string Title, string Category, string Command, params string[] Aliases)
{
    public bool Matches(string command) => command == Command || Aliases.Contains(command);
}

public static class ShortcutCatalog
{
    static string Tool(string id) => "script-message-to mpvnet modern-tool " + id;
    public static IReadOnlyList<ShortcutAction> All { get; } = new ShortcutAction[] {
        new("pause", "播放 / 暂停", "播放", "script-message-to mpvnet play-pause", "cycle pause"),
        new("stop", "停止播放", "播放", "stop"),
        new("hold-back", "长按连续快退", "跳转", "script-binding mpvnet/modern-hold-back"),
        new("hold-forward", "长按连续快进", "跳转", "script-binding mpvnet/modern-hold-forward"),
        new("hold-temporary", "独立长按快进（临时倍率）", "跳转", "script-binding mpvnet/modern-hold-temporary"),
        new("frame-back", "上一帧", "逐帧", "frame-back-step", Tool("frame-back")),
        new("frame-forward", "下一帧", "逐帧", "frame-step", Tool("frame-forward")),
        new("previous", "上一个视频", "播放列表", "playlist-prev"),
        new("next", "下一个视频", "播放列表", "playlist-next"),
        new("playlist", "显示 / 隐藏播放列表", "播放列表", Tool("playlist"), "script-binding select/select-playlist"),
        new("volume-up", "音量增加 2%", "声音", "add volume 2"),
        new("volume-down", "音量降低 2%", "声音", "add volume -2"),
        new("mute", "静音 / 恢复声音", "声音", "cycle mute"),
        new("speed-up", "倍速增加 10%", "倍速", "multiply speed 1.1"),
        new("speed-down", "倍速降低 10%", "倍速", "multiply speed 1/1.1"),
        new("speed-reset", "恢复 1.00 倍", "倍速", "set speed 1"),
        new("fullscreen", "全屏 / 退出全屏", "窗口", "cycle fullscreen"),
        new("ontop", "窗口置顶 / 取消置顶", "窗口", "cycle ontop"),
        new("window-reset", "还原初始窗口大小", "窗口", Tool("window-reset")),
        new("zoom-reset", "还原画面大小与位置", "画面", Tool("zoom-reset"), "set video-zoom 0; set video-pan-x 0; set video-pan-y 0"),
        new("picture", "打开亮度与夜间护眼调节", "画面", Tool("picture")),
        new("night-mode", "开启 / 关闭夜间护眼", "画面", Tool("night-mode")),
        new("screenshot", "截图（含字幕）", "截图", Tool("screenshot"), "screenshot", "screenshot subtitles"),
        new("screenshot-video", "截图（不含字幕）", "截图", Tool("screenshot-video"), "screenshot video"),
        new("open", "打开视频", "文件", "script-message-to mpvnet open-files"),
        new("settings", "打开设置", "设置", "script-message-to mpvnet show-conf-editor")
    };
    public static ShortcutAction? Find(string id) => All.FirstOrDefault(item => item.Id == id);
}
