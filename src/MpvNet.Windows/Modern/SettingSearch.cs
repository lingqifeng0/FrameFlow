namespace MpvNet.Windows.Modern;

public static class SettingSearch
{
    public static bool Matches(Setting setting, string search)
    {
        string text = string.Join(' ', setting.Name, setting.DisplayTitle, setting.Directory, setting.Help, setting.Value, Aliases(setting));
        if (setting.File == "mpvnet") text += " FrameFlow 播放器专用参数";
        if (setting is OptionSetting option)
            text += " " + string.Join(' ', option.Options.Select(value => string.Join(' ', value.Name, value.Text, value.Help)));
        return search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static int Rank(Setting setting, string search) =>
        string.Equals(setting.Name, search, StringComparison.OrdinalIgnoreCase) ? 3 :
        (setting.DisplayTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
         Aliases(setting).Contains(search, StringComparison.OrdinalIgnoreCase)) ? 2 : 1;

    static string Aliases(Setting setting) => setting.Name switch {
        "speed" or "modern.remember-speed" => "倍速 倍率 播放速度 快放 慢放",
        "modern.remember-position" or "save-position-on-quit" or "resume-playback" => "断点 续播 播放进度 记忆",
        "hwdec" => "硬解 硬件解码 显卡 GPU",
        "video-zoom" => "放大 缩小 缩放",
        "fullscreen" => "全屏",
        "ontop" => "置顶 最前面",
        "osd-level" => "提示 反馈 OSD",
        _ => setting.Name?.StartsWith("screenshot", StringComparison.OrdinalIgnoreCase) == true ? "截图 截屏 保存图片" : ""
    };
}
