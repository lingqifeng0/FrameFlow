using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MpvNet.Windows.Modern;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var header = new RoundedPanel { Size = new Size(1200, 44), BackColor = Color.FromArgb(0,150,255) };
        int checks = 0;
        void Check(bool condition, string title) {
            if (!condition) throw new Exception(title);
            checks++;
            Console.WriteLine("PASS: " + title);
        }
        Color CenterPixel() {
            using var image = new Bitmap(header.Width, header.Height);
            header.DrawToBitmap(image, header.ClientRectangle);
            return image.GetPixel(header.Width/2, header.Height/2);
        }
        Check(CenterPixel().ToArgb() == Color.FromArgb(0,150,255).ToArgb(), "normal header renders the requested blue");
        Check(header.Region != null && !header.Region.IsVisible(0,0) && header.Region.IsVisible(header.Width/2,header.Height/2),
            "normal header clips its corners while retaining its center");
        header.BackColor = Color.Black;
        header.ResizeRenderingSuspended = true;
        int before = header.RegionUpdates;
        Check(header.Region == null, "live resize clears the header region once");
        for (int i=0; i<50; i++) {
            header.Width = 520 + (i*71)%1050;
            using var image = new Bitmap(header.Width,header.Height);
            header.DrawToBitmap(image,header.ClientRectangle);
            for (int y=0;y<image.Height;y++)
                for (int x=0;x<image.Width;x++)
                    if (image.GetPixel(x,y).ToArgb()!=Color.Black.ToArgb())
                        throw new Exception($"Live resize left a colored header pixel at {i}:{x},{y}");
        }
        Check(header.RegionUpdates==before && header.Region==null, "50 resized renders remain plain dark without repeated native clipping");
        header.BackColor = Color.FromArgb(0,150,255);
        header.ResizeRenderingSuspended = false;
        Check(header.RegionUpdates==before+1 && header.Region!=null, "release restores the rounded region with one update");
        Check(CenterPixel().ToArgb()==Color.FromArgb(0,150,255).ToArgb(), "release restores blue at the final header size");
        Check(!header.Region!.IsVisible(header.Width-1,0) && header.Region.IsVisible(header.Width-12,header.Height/2),
            "the restored right corner follows the final width");
        header.ResizeRenderingSuspended=false;
        Check(header.RegionUpdates==before+1, "repeated release does not repaint the native region again");
        var origin = Screen.PrimaryScreen!.WorkingArea.Location + new Size(40,40);
        using var host = new ResizeTestForm {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            AutoScaleMode = AutoScaleMode.None,
            Bounds = new Rectangle(origin.X,origin.Y,1100,720), Padding = new Padding(4), BackColor = Color.Black
        };
        var top = new PaintProbe { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(13,15,19) };
        var bottom = new PaintProbe { Dock = DockStyle.Bottom, Height = 87, BackColor = Color.FromArgb(13,15,19) };
        var body = new PaintProbe { Dock = DockStyle.Fill, Padding = new Padding(4), BackColor = Color.Black };
        var videoChild = new PaintProbe { Dock = DockStyle.Fill, BackColor = Color.Black };
        body.Controls.Add(videoChild);
        host.Controls.Add(body); host.Controls.Add(bottom); host.Controls.Add(top);
        host.Show(); // Only this test's own non-activating HWNDs; no desktop input.
        var children = new[] { top, bottom, body, videoChild };
        for (int i = 0; i < 120; i++) {
            host.SuspendLayout();
            // Moving top/left as well as growing/shrinking reproduces the exposed
            // dirty areas which a center-pixel DrawToBitmap check cannot detect.
            host.Bounds = new Rectangle(origin.X+i%19,origin.Y+i%23,620+i*71%850,420+i*47%520);
            host.ResumeLayout(true);
            var counts = children.Select(c => c.PaintCount).ToArray();
            if (!WindowResizeRendering.Repaint(host, videoChild)) throw new Exception("Native hierarchy repaint failed");
            for (int n=0; n<children.Length; n++) {
                var child = children[n];
                if (child == videoChild && child.PaintCount != counts[n])
                    throw new Exception("Host redraw erased/repainted the GPU subtree");
                if (child != videoChild && (child.PaintCount <= counts[n] || !child.LastClip.Contains(child.ClientRectangle)))
                    throw new Exception($"Repaint left incomplete child {n} at intermediate frame {i}: paints {counts[n]}->{child.PaintCount}, clip {child.LastClip}, client {child.ClientRectangle}, visible {child.Visible}, host {host.Bounds}");
                GetWindowRect(child.Handle, out var native);
                if (Rectangle.FromLTRB(native.Left,native.Top,native.Right,native.Bottom) != child.RectangleToScreen(child.ClientRectangle))
                    throw new Exception($"Native/managed child position diverged at {i}:{n}");
            }
            if (top.Left != 4 || top.Top != 4 || top.Right != host.ClientSize.Width-4 ||
                bottom.Bottom != host.ClientSize.Height-4 || body.Top != top.Bottom || body.Bottom != bottom.Top)
                throw new Exception($"Resize moved a docked bar outside its reserved area at {i}");
        }
        Check(true,"120 moving/growing/shrinking native frames repaint the chrome while excluding the GPU subtree");
        Check(true,"native child coordinates agree with docking through all 120 intermediate frames");
        host.Close();
        Console.WriteLine($"All {checks} rendering checks passed (50 intermediate sizes).");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [StructLayout(LayoutKind.Sequential)]
    struct WindowPosition { public IntPtr Window, InsertAfter; public int X,Y,Width,Height; public uint Flags; }
    sealed class ResizeTestForm : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0046 && message.LParam != IntPtr.Zero) {
                var position = Marshal.PtrToStructure<WindowPosition>(message.LParam);
                position.Flags |= 0x0100; // The same SWP_NOCOPYBITS used by the player.
                Marshal.StructureToPtr(position,message.LParam,false);
            }
            if (message.Msg == 0x0083) { message.Result=IntPtr.Zero; return; }
            base.WndProc(ref message);
        }
    }
    sealed class PaintProbe : BufferedPanel
    {
        public int PaintCount { get; private set; }
        public Rectangle LastClip { get; private set; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            PaintCount++;
            LastClip = e.ClipRectangle;
            base.OnPaintBackground(e);
        }
    }
}
