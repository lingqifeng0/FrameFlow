namespace MpvNet.Windows.Modern;

public static class WheelActions
{
    // Id/title choices share the small mouse-action view model used by the
    // double-click pickers; wheel dispatch itself lives in the input handler.
    public static readonly DoubleClickAction[] All =
    [
        new("volume", "调节音量"),
        new("zoom", "缩放画面（围绕鼠标位置）"),
        new("speed", "调节播放倍率"),
        new("seek", "前进 / 后退（使用轻点位移）"),
        new("none", "不执行操作"),
        new("input-conf", "跟随快捷键配置")
    ];
    public static string Normalize(string? action, bool control) => All.Any(item => item.Id == action)
        ? action! : control ? "zoom" : "volume";
}
