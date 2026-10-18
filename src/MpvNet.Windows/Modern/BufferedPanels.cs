using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MpvNet.Windows.Modern;

// Buffer the chrome alone. Compositing the entire form would also affect mpv's
// separate GPU window and can interfere with live video presentation.
public class BufferedPanel : Panel
{
    public BufferedPanel() => SetStyle(ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
}

public class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel() => SetStyle(ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
}

// Paint the rounded bar with antialiasing. A native window region can only
// keep/discard whole pixels and would cut off the smooth edge. Child controls
// occupy the rectangular middle; the host owns the separate outer contour.
public sealed class RoundedPanel : Panel
{
    bool _roundedCorners = true;
    public int CornerRadius => _roundedCorners ? 10 : 0;
    public bool RoundedCorners
    {
        get => _roundedCorners;
        set {
            if (_roundedCorners == value) return;
            _roundedCorners = value;
            _regionSize = Size.Empty;
            UpdateRoundedGeometry();
            Invalidate();
        }
    }
    public int RegionUpdates { get; private set; }
    Size _regionSize;
    int _regionDpi;
    bool _resizeRenderingSuspended;

    public bool ResizeRenderingSuspended
    {
        get => _resizeRenderingSuspended;
        set {
            if (_resizeRenderingSuspended == value) return;
            _resizeRenderingSuspended = value;
            _regionSize = Size.Empty;
            _regionDpi = 0;
            if (!value) UpdateRoundedGeometry();
            Invalidate();
        }
    }

    public RoundedPanel() => SetStyle(ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedGeometry();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateRoundedGeometry();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        // DPI scaling may change Padding after the first size notification.
        // Reconcile the safe inset before docking the child content.
        UpdateRoundedGeometry();
        base.OnLayout(e);
    }

    void UpdateRoundedGeometry()
    {
        if (_resizeRenderingSuspended || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        if (_regionSize != ClientSize || _regionDpi != DeviceDpi) {
            _regionSize = ClientSize;
            _regionDpi = DeviceDpi;
            // Keep the existing diagnostic counter for geometry updates.
            RegionUpdates++;
            Invalidate();
        }
        int inset = (int)Math.Ceiling(Math.Min(Math.Min(Width, Height) / 2f, CornerRadius * DeviceDpi / 96f));
        var safePadding = new Padding(inset, 0, inset, 0);
        if (Padding != safePadding) Padding = safePadding;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Black);
        if (_resizeRenderingSuspended || BackColor.ToArgb() == Color.Black.ToArgb()) return;
        if (!RoundedCorners) {
            e.Graphics.Clear(BackColor);
            return;
        }
        float diameter = Math.Min(Math.Min(ClientSize.Width, ClientSize.Height), CornerRadius * 2 * DeviceDpi / 96f);
        if (diameter <= 0) return;
        using var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(ClientSize.Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(ClientSize.Width - diameter, ClientSize.Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, ClientSize.Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        var state = e.Graphics.Save();
        try {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillPath(brush, path);
        } finally { e.Graphics.Restore(state); }
    }
}
