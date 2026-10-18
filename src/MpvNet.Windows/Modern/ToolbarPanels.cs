using System.Drawing;
using System.Windows.Forms;

namespace MpvNet.Windows.Modern;

public sealed class ToolbarPanel : BufferedPanel
{
    public bool LeadingDivider { get; set; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ToolbarSeparators.Draw(this, e.Graphics, LeadingDivider);
    }
}

public sealed class ToolbarFlowPanel : BufferedFlowLayoutPanel
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ToolbarSeparators.Draw(this, e.Graphics);
    }
}

static class ToolbarSeparators
{
    public static void Draw(Control owner, Graphics graphics, bool leading = false)
    {
        var buttons = owner.Controls.OfType<ModernButton>().Where(button => button.Visible)
            .OrderBy(button => button.Left).ToArray();
        if (buttons.Length == 0) return;
        if (leading && buttons[0].Left >= 10)
            DrawLine(graphics, buttons[0].Left - 8, buttons[0].Bounds, true);
        for (int i = 1; i < buttons.Length; i++) {
            var left = buttons[i - 1];
            var right = buttons[i];
            int gap = right.Left - left.Right;
            // Only neighbors get a divider: never paint slots, trailing space,
            // or the gap between the custom toolbar and the right-hand group.
            if (gap < 2 || gap > Math.Max(16, right.Height / 2)) continue;
            DrawLine(graphics, left.Right + gap / 2, right.Bounds, left.SeparatorGroup != right.SeparatorGroup);
        }
    }

    static void DrawLine(Graphics graphics, int x, Rectangle bounds, bool group)
    {
        int inset = Math.Max(2, (int)Math.Round(bounds.Height * (group ? 4 : 7) / 36d));
        using var pen = new Pen(group ? Color.FromArgb(75, 83, 98) : Color.FromArgb(49, 56, 68));
        graphics.DrawLine(pen, x, bounds.Top + inset, x, bounds.Bottom - inset);
    }
}
