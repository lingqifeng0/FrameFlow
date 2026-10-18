using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MpvNet.Windows.Modern;

// A single opaque CPU surface owns every visible pixel during a resize. The
// GPU HWND and the docked chrome retain their previous sizes behind it.
public sealed class ResizePreviewCanvas : Control
{
    Bitmap? _frame;
    Rectangle _source;
    public Padding VideoInsets { get; set; }
    public int PaintCount { get; private set; }
    public bool HasFrame => _frame != null;
    public ResizePreviewCanvas()
    {
        BackColor = Color.Black;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
    }
    public void SetFrame(Bitmap? frame, Rectangle source)
    {
        var previous = _frame;
        _frame = frame;
        _source = source;
        previous?.Dispose();
        Invalidate();
    }
    public Rectangle VideoRectangle => new(VideoInsets.Left, VideoInsets.Top,
        Math.Max(0, Width - VideoInsets.Horizontal), Math.Max(0, Height - VideoInsets.Vertical));
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.Black);
        if (_frame != null && _source.Width > 0 && _source.Height > 0) {
            var area = VideoRectangle;
            double scale = Math.Min(area.Width / (double)_source.Width, area.Height / (double)_source.Height);
            int width = Math.Max(1, (int)Math.Round(_source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(_source.Height * scale));
            var destination = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
            e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(_frame, destination, _source, GraphicsUnit.Pixel);
        }
        PaintCount++;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _frame?.Dispose(); _frame = null; }
        base.Dispose(disposing);
    }
}
