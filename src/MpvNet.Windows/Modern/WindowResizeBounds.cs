using System.Drawing;

namespace MpvNet.Windows.Modern;

public static class WindowResizeBounds
{
    // Edges: left, right, top, bottom, top-left, top-right, bottom-left, bottom-right.
    public static Rectangle Drag(Rectangle original, int edge, int dx, int dy, Size minimum)
    {
        bool left = edge is 0 or 4 or 6, right = edge is 1 or 5 or 7;
        bool top = edge is 2 or 4 or 5, bottom = edge is 3 or 6 or 7;
        int width = left || right ? Math.Max(minimum.Width, original.Width + (left ? -dx : dx)) : original.Width;
        int height = top || bottom ? Math.Max(minimum.Height, original.Height + (top ? -dy : dy)) : original.Height;
        return new Rectangle(left ? original.Right - width : original.Left,
            top ? original.Bottom - height : original.Top, width, height);
    }
}
