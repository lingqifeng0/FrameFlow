using System.Drawing;
using System.Windows.Forms;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    ResizePreviewCanvas? _resizePreview;
    System.Windows.Forms.Timer? _resizeRevealTimer;
    long _resizeRevealStarted;
    int _resizePreviewCaptures, _resizePreviewFailures, _resizeVideoCommits, _resizeRevealTimeouts;
    Size _resizeFrozenVideoSize;

    void BeginModernResizePreview()
    {
        _resizeRevealTimer?.Stop();
        EndHeldNavigation(false);
        CancelModernScrubbing();
        Bitmap? frame = null;
        Rectangle source = Rectangle.Empty;
        if (Player.Handle != IntPtr.Zero && Player.GetPropertyInt("video-params/w") > 0) {
            try {
                frame = ScreenshotImage.CaptureBitmap(Player,"window",1600);
                double scaleX=frame.Width/(double)Math.Max(1,Player.GetPropertyInt("osd-dimensions/w"));
                double scaleY=frame.Height/(double)Math.Max(1,Player.GetPropertyInt("osd-dimensions/h"));
                int left = Math.Clamp((int)Math.Round(Player.GetPropertyDouble("osd-dimensions/ml")*scaleX), 0, frame.Width - 1);
                int top = Math.Clamp((int)Math.Round(Player.GetPropertyDouble("osd-dimensions/mt")*scaleY), 0, frame.Height - 1);
                int right = Math.Clamp((int)Math.Round(Player.GetPropertyDouble("osd-dimensions/mr")*scaleX), 0, frame.Width - left - 1);
                int bottom = Math.Clamp((int)Math.Round(Player.GetPropertyDouble("osd-dimensions/mb")*scaleY), 0, frame.Height - top - 1);
                source = new Rectangle(left, top, frame.Width - left - right, frame.Height - top - bottom);
                _resizePreviewCaptures++;
            } catch { frame?.Dispose(); frame = null; _resizePreviewFailures++; }
        }
        _resizePreview ??= new ResizePreviewCanvas { Visible = false, Name = "ResizePreview" };
        if (_resizePreview.Parent == null) Controls.Add(_resizePreview);
        _resizePreview.SetFrame(frame, source);
        _resizePreview.VideoInsets = new Padding(0, _modernHeader.Height,
            _playlistPane.Visible ? _playlistPane.Width : 0, _modernFooter.Height);
        _resizeFrozenVideoSize = _videoSurface.ClientSize;
        // Dock=None restores a control's earlier undocked rectangle. Suspend
        // child layout and explicitly retain the current rectangles so even
        // this transition cannot shrink the GPU to its initial 200x100 size.
        _modernBody.SuspendLayout();
        SuspendLayout();
        try {
            // None of these HWNDs is resized during the gesture. Hiding only
            // their contents still left three independent background surfaces.
            foreach (var control in new Control[] { _modernBody, _modernHeader, _modernFooter }) {
                var bounds = control.Bounds;
                control.Dock = DockStyle.None;
                control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                control.Bounds = bounds;
            }
            _resizePreview.Dock = DockStyle.Fill;
            _resizePreview.Visible = true;
            _resizePreview.BringToFront();
            BackColor = Color.Black;
        } finally { ResumeLayout(true); _modernBody.ResumeLayout(true); }
        _resizePreview.Update();
        _modernBody.Visible = false;
        _modernHeader.Visible = false;
        _modernFooter.Visible = false;
    }

    void RestoreModernResizeSurfaces()
    {
        if (_resizePreview == null || !_resizePreview.Visible) return;
        // The cover no longer participates in docking, but remains above the
        // final layout until the GPU confirms a rendered frame at the new size.
        _resizePreview.Dock = DockStyle.None;
        _resizePreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _resizePreview.Bounds = DisplayRectangle;
        _modernHeader.Dock = DockStyle.Top;
        _modernFooter.Dock = DockStyle.Bottom;
        _modernBody.Dock = DockStyle.Fill;
        Controls.SetChildIndex(_modernBody, 1);
        Controls.SetChildIndex(_modernFooter, 2);
        Controls.SetChildIndex(_modernHeader, 3);
        _modernBody.Visible = _modernHeader.Visible = _modernFooter.Visible = true;
        _resizePreview.BringToFront();
        BackColor = ModernTheme.Background;
        _resizeVideoCommits++;
    }

    void AwaitModernResizeFrame()
    {
        if (_resizePreview?.Visible != true) return;
        if (!_resizePreview.HasFrame || Player.GetPropertyInt("video-params/w") <= 0) { FinishModernResizePreview(); return; }
        _resizeRevealStarted = Environment.TickCount64;
        _resizeRevealTimer ??= new System.Windows.Forms.Timer { Interval = 16 };
        if (_resizeRevealTimer.Tag == null) {
            _resizeRevealTimer.Tag = true;
            _resizeRevealTimer.Tick += (_, _) => {
                if (_modernInteractiveResize || _modernClosing) { _resizeRevealTimer.Stop(); return; }
                bool ready = Player.GetPropertyInt("osd-dimensions/w") == _videoSurface.ClientSize.Width &&
                    Player.GetPropertyInt("osd-dimensions/h") == _videoSurface.ClientSize.Height;
                if (ready) {
                    try {
                        var rendered = Player.CaptureScreenshotDimensions("window");
                        ready = rendered.Width == _videoSurface.ClientSize.Width && rendered.Height == _videoSurface.ClientSize.Height;
                    } catch { ready = false; }
                }
                if (ready) FinishModernResizePreview();
                else if (Environment.TickCount64 - _resizeRevealStarted > 600) {
                    _resizeRevealTimeouts++;
                    FinishModernResizePreview();
                }
            };
        }
        _resizeRevealTimer.Start();
    }

    void FinishModernResizePreview()
    {
        _resizeRevealTimer?.Stop();
        if (_resizePreview == null || !_resizePreview.Visible) return;
        _resizePreview.Visible = false;
        _resizePreview.SetFrame(null, Rectangle.Empty);
        Controls.SetChildIndex(_modernBody, 0);
        Controls.SetChildIndex(_modernFooter, 1);
        Controls.SetChildIndex(_modernHeader, 2);
        RepaintModernWindow();
        PublishModernWindowState();
    }
}
