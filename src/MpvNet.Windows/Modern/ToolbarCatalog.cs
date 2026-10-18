using System.Globalization;

namespace MpvNet.Windows.Modern;

public sealed record ToolParameter(string Label, double Minimum, double Maximum, string TitleFormat, int Sign = 1, int Decimals = 2);

public sealed record ToolDefinition(string Id, string Title, string Category, string Glyph, double Value = 0, ToolParameter? Parameter = null)
{
    public ToolbarItem Item => new(Id, Title, Value);
    public ToolbarItem Parameterize(double value)
    {
        if (Parameter == null) return Item;
        if (!double.IsFinite(value) || value < Parameter.Minimum || value > Parameter.Maximum)
            throw new ArgumentOutOfRangeException(nameof(value), $"请输入 {Parameter.Minimum}–{Parameter.Maximum} 范围内的数值");
        value = Math.Round(value, Parameter.Decimals);
        return new(Id, string.Format(CultureInfo.InvariantCulture, Parameter.TitleFormat, value), value * Parameter.Sign);
    }
}

public static class ToolbarCatalog
{
    static ToolParameter Seconds(string format, int sign) => new("跳转秒数", 0.01, 3600, format, sign);
    static ToolParameter Delay(string format, int sign) => new("每次调整秒数", 0.01, 60, format, sign);
    static ToolParameter Zoom(string format, int sign) => new("每次调整幅度", 0.01, 1, format, sign);
    public static IReadOnlyList<ToolDefinition> All { get; } = new ToolDefinition[] {
        new("frame-back", "上一帧", "播放与跳转", "\uE892"),
        new("frame-forward", "下一帧", "播放与跳转", "\uE893"),
        new("seek-back", "后退 5 秒", "播放与跳转", "\uE72B", -5, Seconds("后退 {0} 秒", -1)),
        new("seek-forward", "前进 5 秒", "播放与跳转", "\uE72A", 5, Seconds("前进 {0} 秒", 1)),
        new("seek-start", "跳到开头", "播放与跳转", "\uE892"),
        new("jump-time", "跳到指定时间", "播放与跳转", "\uE823"),
        new("chapter-back", "上一章节", "播放与跳转", "\uE892"),
        new("chapter-forward", "下一章节", "播放与跳转", "\uE893"),
        new("loop-file", "单个视频循环", "播放与跳转", "\uE8ED"),
        new("loop-playlist", "播放列表循环", "播放与跳转", "\uE8EE"),
        new("stop", "停止播放", "播放与跳转", "\uE71A"),
        new("speed", "设为 1.50 倍", "倍速", "\uE945", 1.5, new("播放倍率", (double)ModernSettings.MinimumSpeed, (double)ModernSettings.MaximumSpeed, "设为 {0:0.00} 倍")),
        new("speed-up", "倍速增加（按设定步长）", "倍速", "\uE710"),
        new("speed-down", "倍速降低（按设定步长）", "倍速", "\uE738"),
        new("speed-reset", "恢复 1.00 倍", "倍速", "\uE777"),
        new("bookmark", "添加书签", "书签与截图", "\uE734"),
        new("bookmarks", "书签列表", "书签与截图", "\uE8A4"),
        new("timestamp", "复制时间戳", "书签与截图", "\uE8C8"),
        new("screenshot", "截图（含字幕）", "书签与截图", "\uE722"),
        new("screenshot-video", "截图（不含字幕）", "书签与截图", "\uE722"),
        new("ab-loop", "AB 循环", "书签与截图", "\uE8EE"),
        new("ab-clear", "取消 AB 循环", "书签与截图", "\uE711"),
        new("sub-toggle", "显示 / 隐藏字幕", "字幕", "\uE8F2"),
        new("sub-next", "切换下一条字幕", "字幕", "\uE8F2"),
        new("sub-load", "加载字幕文件", "字幕", "\uE8E5"),
        new("sub-earlier", "字幕提前 0.10 秒", "字幕", "\uE72B", -.1, Delay("字幕提前 {0:0.00} 秒", -1)),
        new("sub-later", "字幕延后 0.10 秒", "字幕", "\uE72A", .1, Delay("字幕延后 {0:0.00} 秒", 1)),
        new("sub-delay-reset", "还原字幕同步", "字幕", "\uE777"),
        new("sub-size-up", "增大字幕", "字幕", "\uE8E8"),
        new("sub-size-down", "缩小字幕", "字幕", "\uE8E9"),
        new("sub-size-reset", "还原字幕大小", "字幕", "\uE777"),
        new("sub-up", "字幕上移", "字幕", "\uE70E"),
        new("sub-down", "字幕下移", "字幕", "\uE70D"),
        new("audio-next", "切换下一条音轨", "音频", "\uE8D6"),
        new("mute", "静音 / 恢复声音", "音频", "\uE74F"),
        new("volume-up", "音量增加 5%", "音频", "\uE767", 5, new("每次调整音量", 1, 100, "音量增加 {0}%", 1, 0)),
        new("volume-down", "音量降低 5%", "音频", "\uE767", -5, new("每次调整音量", 1, 100, "音量降低 {0}%", -1, 0)),
        new("audio-earlier", "声音提前 0.10 秒", "音频", "\uE72B", -.1, Delay("声音提前 {0:0.00} 秒", -1)),
        new("audio-later", "声音延后 0.10 秒", "音频", "\uE72A", .1, Delay("声音延后 {0:0.00} 秒", 1)),
        new("audio-delay-reset", "还原音画同步", "音频", "\uE777"),
        new("zoom-in", "画面放大", "画面", "\uE8A3", .1, Zoom("画面放大 {0:0.00}", 1)),
        new("zoom-out", "画面缩小", "画面", "\uE71F", -.1, Zoom("画面缩小 {0:0.00}", -1)),
        new("zoom-reset", "还原画面缩放与位置", "画面", "\uE777"),
        new("pan-left", "画面左移", "画面", "\uE76B"),
        new("pan-right", "画面右移", "画面", "\uE76C"),
        new("pan-up", "画面上移", "画面", "\uE70E"),
        new("pan-down", "画面下移", "画面", "\uE70D"),
        new("rotate", "画面旋转 90°", "画面", "\uE7AD"),
        new("aspect-original", "还原视频比例", "画面", "\uE799"),
        new("aspect-16-9", "画面比例 16:9", "画面", "\uE799"),
        new("aspect-4-3", "画面比例 4:3", "画面", "\uE799"),
        new("brightness-up", "增加亮度", "画面", "\uE706"),
        new("picture", "画面调节（亮度 / 夜间护眼）", "画面", "\uE706"),
        new("night-mode", "夜间护眼开关", "画面", "\uE708"),
        new("brightness-down", "降低亮度", "画面", "\uE706"),
        new("contrast-up", "增加对比度", "画面", "\uE793"),
        new("contrast-down", "降低对比度", "画面", "\uE793"),
        new("saturation-up", "增加饱和度", "画面", "\uE790"),
        new("saturation-down", "降低饱和度", "画面", "\uE790"),
        new("picture-reset", "还原所有画面调整", "画面", "\uE777"),
        new("ontop", "窗口置顶", "窗口与文件", "\uE718"),
        new("fullscreen", "进入 / 退出全屏", "窗口与文件", "\uE740"),
        new("window-reset", "还原初始窗口大小", "窗口与文件", "\uE777"),
        new("playlist", "展开 / 收起播放列表", "窗口与文件", "\uE8FD"),
        new("media-info", "视频信息", "窗口与文件", "\uE946"),
        new("open-video", "打开视频", "窗口与文件", "\uE8E5"),
        new("settings", "播放器设置", "窗口与文件", "\uE713")
    };
    public static ToolDefinition? Find(string id) => All.FirstOrDefault(t => t.Id == id);
    public static IEnumerable<ToolDefinition> Filter(string category, string search) => All.Where(t =>
        (category == "全部功能" || category == t.Category) &&
        (string.IsNullOrWhiteSpace(search) || (t.Title + t.Category).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)));

    // Native mpv commands are shared by the toolbar and the external modern-tool API.
    public static IReadOnlyList<string[]> Commands(ToolbarItem item)
    {
        var value = item.Value.ToString(CultureInfo.InvariantCulture);
        return item.Id switch {
            "seek-back" or "seek-forward" => [["seek", value, "relative+exact"]],
            "seek-start" => [["seek", "0", "absolute+exact"]],
            "chapter-back" => [["add", "chapter", "-1"]], "chapter-forward" => [["add", "chapter", "1"]],
            "loop-file" => [["cycle-values", "loop-file", "inf", "no"]],
            "loop-playlist" => [["cycle-values", "loop-playlist", "inf", "no"]],
            "stop" => [["stop"]],
            "sub-toggle" => [["cycle", "sub-visibility"]], "sub-next" => [["cycle", "sid"]],
            "sub-earlier" or "sub-later" => [["add", "sub-delay", value]],
            "sub-delay-reset" => [["set", "sub-delay", "0"]],
            "sub-size-up" => [["add", "sub-scale", "0.1"]], "sub-size-down" => [["add", "sub-scale", "-0.1"]],
            "sub-size-reset" => [["set", "sub-scale", "1"]],
            "sub-up" => [["add", "sub-pos", "-5"]], "sub-down" => [["add", "sub-pos", "5"]],
            "audio-next" => [["cycle", "aid"]], "mute" => [["cycle", "mute"]],
            "volume-up" or "volume-down" => [["add", "volume", value]],
            "audio-earlier" or "audio-later" => [["add", "audio-delay", value]],
            "audio-delay-reset" => [["set", "audio-delay", "0"]],
            "zoom-in" or "zoom-out" => [["add", "video-zoom", value]],
            "zoom-reset" => [["set", "video-zoom", "0"], ["set", "video-pan-x", "0"], ["set", "video-pan-y", "0"]],
            "pan-left" => [["add", "video-pan-x", "-0.05"]], "pan-right" => [["add", "video-pan-x", "0.05"]],
            "pan-up" => [["add", "video-pan-y", "-0.05"]], "pan-down" => [["add", "video-pan-y", "0.05"]],
            "rotate" => [["cycle-values", "video-rotate", "90", "180", "270", "0"]],
            "aspect-original" => [["set", "video-aspect-override", "-1"]],
            "aspect-16-9" => [["set", "video-aspect-override", (16d / 9).ToString(CultureInfo.InvariantCulture)]],
            "aspect-4-3" => [["set", "video-aspect-override", (4d / 3).ToString(CultureInfo.InvariantCulture)]],
            "brightness-up" => [["add", "brightness", "5"]], "brightness-down" => [["add", "brightness", "-5"]],
            "contrast-up" => [["add", "contrast", "5"]], "contrast-down" => [["add", "contrast", "-5"]],
            "saturation-up" => [["add", "saturation", "5"]], "saturation-down" => [["add", "saturation", "-5"]],
            "fullscreen" => [["cycle", "fullscreen"]],
            _ => []
        };
    }
}
