namespace MpvNet.Windows.Modern;

public sealed record DoubleClickAction(string Id, string Title);

public static class DoubleClickActions
{
    public const string DefaultVideo = "pause";
    public const string DefaultTitleBar = "maximize";

    public static readonly DoubleClickAction[] TitleBar =
    [
        new("fullscreen", "全屏 / 退出全屏"),
        new("maximize", "最大化 / 还原"),
        new("pause", "播放 / 暂停"),
        new("none", "不执行操作")
    ];

    public static readonly DoubleClickAction[] Video =
    [
        .. TitleBar,
        new("input-conf", "跟随快捷键配置")
    ];

    public static string Normalize(string? action, bool video) =>
        (video ? Video : TitleBar).Any(item => item.Id == action)
            ? action!
            : video ? DefaultVideo : DefaultTitleBar;
}
