using System.Drawing;
using System.Drawing.Drawing2D;

namespace MpvNet.Windows.Modern;

public enum ModernIcon { FontGlyph, PreviousVideo, NextVideo, PreviousFrame, NextFrame }

public static class ModernIcons
{
    public static void Draw(Graphics graphics, ModernIcon icon, Rectangle bounds, Color color, float scale)
    {
        if (icon == ModernIcon.FontGlyph) return;
        using var brush = new SolidBrush(color);
        float cx = bounds.Left + bounds.Width / 2f, cy = bounds.Top + bounds.Height / 2f;
        int direction = icon is ModernIcon.PreviousVideo or ModernIcon.PreviousFrame ? -1 : 1;
        if (icon is ModernIcon.PreviousVideo or ModernIcon.NextVideo) {
            graphics.FillPolygon(brush, new PointF[] {
                new(cx - 8 * direction * scale, cy - 9 * scale),
                new(cx + 5 * direction * scale, cy),
                new(cx - 8 * direction * scale, cy + 9 * scale)
            });
            graphics.FillRectangle(brush, cx + (direction > 0 ? 8 : -11) * scale, cy - 9 * scale, 3 * scale, 18 * scale);
            return;
        }
        using var pen = new Pen(color, 1.4f * scale) { LineJoin = LineJoin.Round };
        using var frame = new GraphicsPath();
        float x = cx - 12 * scale, y = cy - 9 * scale, w = 24 * scale, h = 18 * scale, d = 4 * scale;
        frame.AddArc(x, y, d, d, 180, 90);
        frame.AddArc(x + w - d, y, d, d, 270, 90);
        frame.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        frame.AddArc(x, y + h - d, d, d, 90, 90);
        frame.CloseFigure();
        graphics.DrawPath(pen, frame);
        graphics.DrawPolygon(pen, new PointF[] {
            new(cx - 4 * direction * scale, cy - 5 * scale),
            new(cx + 3 * direction * scale, cy),
            new(cx - 4 * direction * scale, cy + 5 * scale)
        });
        graphics.DrawLine(pen, cx + 7 * direction * scale, cy - 5 * scale, cx + 7 * direction * scale, cy + 5 * scale);
    }
}
