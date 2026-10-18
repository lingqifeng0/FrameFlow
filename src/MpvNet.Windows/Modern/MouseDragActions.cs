namespace MpvNet.Windows.Modern;

public static class MouseDragActions
{
    public static readonly DoubleClickAction[] All =
    [new("pan","平移画面"),new("none","不执行操作"),new("input-conf","跟随快捷键配置")];
    public static string Normalize(string? action)=>All.Any(item=>item.Id==action)?action!:"pan";
}
