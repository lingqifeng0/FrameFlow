using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace MpvNet.Windows.Modern;

public static class ModernTheme
{
    static readonly Lazy<System.Drawing.Icon?> _applicationIcon = new(() =>
        System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath));
    public static System.Drawing.Icon? ApplicationIcon => _applicationIcon.Value;
    public static Color Background = Color.FromArgb(13, 15, 19);
    public static Color Surface = Color.FromArgb(23, 26, 33);
    public static Color Hover = Color.FromArgb(40, 46, 58);
    public static Color Text = Color.FromArgb(236, 239, 245);
    public static Color Muted = Color.FromArgb(151, 162, 180);
    public static Color Accent = Color.FromArgb(111, 167, 255);
    public static Color HeaderBackground = Color.FromArgb(0, 150, 255); // #0096FF
    public static Color HeaderForeground = Color.White;
    public static Color HeaderHover = Color.FromArgb(30, 165, 255);
    public static Color HeaderAccent = Color.White;

    public static Label Label(string text, int size = 10, bool muted = false) => new()
    {
        Text = text, ForeColor = muted ? Muted : Text, AutoSize = true,
        Font = new Font("Microsoft YaHei UI", size), Margin = new Padding(0, 5, 0, 5)
    };
    public static void StyleInput(Control c)
    {
        c.BackColor = Surface; c.ForeColor = Text;
        c.Font = new Font("Microsoft YaHei UI", 10);
        if (c is TextBox t) t.BorderStyle = BorderStyle.FixedSingle;
    }
    public static Form Dialog(string title, int width, int height)
    {
        var dialog = new Form {
        Text = title, BackColor = Background, ForeColor = Text, AutoScaleMode = AutoScaleMode.Dpi,
        AutoScaleDimensions = new SizeF(96, 96),
        Font = new Font("Microsoft YaHei UI", 10), StartPosition = FormStartPosition.CenterParent,
        ClientSize = new Size(width, height), MinimumSize = new Size(Math.Min(width, 480), 330),
        ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false,
        Icon = ApplicationIcon
        };
        dialog.HandleCreated += (_, _) => DwmSetWindowAttribute(dialog.Handle, 20, new[] { 1 }, 4);
        return dialog;
    }
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, int[] value, int size);
}

public sealed class ModernButton : Button
{
    public string Glyph { get; set; } = "";
    public ModernIcon Icon { get; set; }
    public float IconSize { get; set; } = 26;
    public string SeparatorGroup { get; set; } = "";
    public bool Accent { get; set; }
    public bool Checked { get; set; }
    public bool IconOnly { get; set; }
    public Color HoverColor { get; set; } = ModernTheme.Hover;
    public Color ActiveColor { get; set; } = ModernTheme.Accent;
    bool hover;
    bool pointerPressed;
    bool clickRaised;
    public ModernButton(string title, string glyph = "", int width = 40)
    {
        Text = title; Glyph = glyph; Width = width; Height = 36;
        AccessibleName = title; AccessibleRole = AccessibleRole.PushButton;
        Cursor = Cursors.Hand; TabStop = false;
        Margin = new Padding(2); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        ForeColor = ModernTheme.Text; BackColor = ModernTheme.Background;
        Font = new Font("Microsoft YaHei UI", 9);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Enabled && ClientRectangle.Contains(e.Location))
        {
            FindForm()?.Activate();
            pointerPressed = Capture;
        }
        base.OnMouseDown(e);
    }
    protected override void OnClick(EventArgs e)
    {
        clickRaised = true;
        base.OnClick(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool releasedInside = pointerPressed && Capture && Enabled &&
            e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
        pointerPressed = false;
        clickRaised = false;
        base.OnMouseUp(e);

        // Remote tools can deliver valid client messages before desktop hit-testing
        // agrees with their window order. Keep Button validation and avoid a second click.
        if (releasedInside && !clickRaised && !IsDisposed && Enabled)
        {
            PerformClick();
            if (clickRaised && !IsDisposed) OnMouseClick(e);
        }
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        pointerPressed = false;
        base.OnMouseCaptureChanged(e);
    }
    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled) pointerPressed = false;
        base.OnEnabledChanged(e);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x001f) pointerPressed = false; // WM_CANCELMODE
        base.WndProc(ref m);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        if (hover || Checked || Accent)
        {
            using var brush = new SolidBrush(Accent ? Color.FromArgb(33, 54, 85) : HoverColor);
            e.Graphics.FillRoundedRectangle(brush, new Rectangle(0, 0, Width - 1, Height - 1), 7);
        }
        var color = !Enabled ? Color.FromArgb(85, 93, 107) : Accent || Checked ? ActiveColor : ForeColor;
        if (Glyph.Length > 0 || Icon != ModernIcon.FontGlyph)
        {
            bool showText = Width > 64 && !IconOnly;
            float scale = Height / 36f;
            int iconWidth = showText ? Math.Min(Width, (int)Math.Round(36 * scale)) : Width;
            var rect = new Rectangle(0, 0, iconWidth, Height);
            // Points grow with desktop DPI even when a host retains a fixed
            // button height. Fit pixel glyphs to the actual control instead.
            float size = Math.Max(1, Math.Min(IconSize * scale, Math.Min(rect.Width, rect.Height) - 8 * scale));
            if (Icon == ModernIcon.FontGlyph) {
                using var font = new Font("Segoe MDL2 Assets", size, FontStyle.Regular, GraphicsUnit.Pixel);
                TextRenderer.DrawText(e.Graphics, Glyph, font, rect, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            } else ModernIcons.Draw(e.Graphics, Icon, rect, color, Math.Min(scale, rect.Width / 40f));
            if (showText) TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(iconWidth, 0, Math.Max(0, Width - iconWidth - 4), Height), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        else TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

public sealed class ModernSeek : Control
{
    public double Position { get; set; }
    public double Duration { get; set; }
    public event Action<double>? SeekRequested;
    public event Action<double>? SeekCompleted;
    public bool IsDragging => dragging;
    bool dragging;
    public ModernSeek()
    {
        Height = 22; Cursor = Cursors.Hand; AccessibleName = "播放进度";
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var basePen = new Pen(Color.FromArgb(57, 63, 74), 3);
        using var fillPen = new Pen(ModernTheme.Accent, 3);
        var x = (float)Math.Clamp(Duration > 0 ? Position / Duration : 0, 0, 1) * Math.Max(1, Width - 12) + 6;
        e.Graphics.DrawLine(basePen, 6, Height / 2, Width - 6, Height / 2);
        e.Graphics.DrawLine(fillPen, 6, Height / 2, x, Height / 2);
        using var brush = new SolidBrush(ModernTheme.Text);
        e.Graphics.FillEllipse(brush, x - 4, Height / 2 - 4, 8, 8);
    }
    void Seek(int x)
    {
        if (Duration <= 0) return;
        Position = Math.Clamp((x - 6.0) / Math.Max(1, Width - 12), 0, 1) * Duration;
        Invalidate(); SeekRequested?.Invoke(Position);
    }
    public void CancelDrag() { dragging = false; Capture = false; }
    void CompleteDrag() {
        if (!dragging) return;
        dragging = false; Capture = false;
        SeekCompleted?.Invoke(Position);
    }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Duration > 0) { dragging = true; Capture = true; Seek(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Seek(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left && dragging) { Seek(e.X); CompleteDrag(); } base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) CompleteDrag(); base.OnMouseCaptureChanged(e); }
}

static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle r, int radius)
    {
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); g.FillPath(brush, path);
    }
}
