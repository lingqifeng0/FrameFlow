using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    bool _videoPanning;
    bool _videoPanSuppressed;
    bool _videoViewGestureUsed;
    Point _videoPanStart;
    double _videoPanStartX;
    double _videoPanStartY;
    SizeF _videoPanSize;

    bool HandleVideoViewInput(Control source, ref Message message)
    {
        if (_modernSettings.CtrlDragAction == "input-conf" ||
            (!_modernSettings.CtrlMouseVideoView && _modernSettings.CtrlDragAction == "pan")) return false;
        bool control = (ModifierKeys & Keys.Control) != 0;
        if (message.Msg is not (0x0200 or 0x0201 or 0x0202 or 0x0203)) return false;
        long packed = message.LParam.ToInt64();
        var point = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
        if (source != _videoSurface) point = _videoSurface.PointToClient(source.PointToScreen(point));
        long flags = message.WParam.ToInt64();
        control |= (flags & 0x0008) != 0; // MK_CONTROL also works for message-based remote input
        if (_modernSettings.CtrlDragAction == "none") {
            if (_videoPanSuppressed) {
                if (message.Msg == 0x0202) { _videoPanSuppressed = false; _videoSurface.Capture = false; return true; }
                if (message.Msg == 0x0200 && (flags & 1) != 0) return true;
            }
            if (control && IsVideoViewPoint(point) && message.Msg is 0x0201 or 0x0203) {
                _videoPanSuppressed = true; _videoSurface.Capture = true; return true;
            }
            return false;
        }

        if (_videoPanning) {
            if (message.Msg == 0x0202) { CancelVideoPan(); return true; }
            if (message.Msg == 0x0200) {
                if ((flags & 0x0001) == 0) { CancelVideoPan(); return true; }
                Player.SetPropertyDouble("video-pan-x", Math.Clamp(_videoPanStartX + (point.X - _videoPanStart.X) / _videoPanSize.Width, -3, 3));
                Player.SetPropertyDouble("video-pan-y", Math.Clamp(_videoPanStartY + (point.Y - _videoPanStart.Y) / _videoPanSize.Height, -3, 3));
                RecordFullscreenPointer(_videoSurface.PointToScreen(point));
                ShowCursor();
                return true;
            }
        }
        if (!control || !IsVideoViewPoint(point)) return false;
        if (message.Msg is 0x0201 or 0x0203 && VideoRectangle() is { Width: > 0, Height: > 0 } rectangle) {
            _videoPanning = true;
            _videoViewGestureUsed = true;
            _videoPanStart = point;
            _videoPanStartX = Player.GetPropertyDouble("video-pan-x");
            _videoPanStartY = Player.GetPropertyDouble("video-pan-y");
            _videoPanSize = rectangle.Size;
            _videoSurface.Cursor = Cursors.SizeAll;
            _videoSurface.Capture = true;
            return true;
        }
        return false;
    }

    RectangleF? VideoRectangle()
    {
        try {
            using var document = JsonDocument.Parse(Player.GetPropertyString("osd-dimensions"));
            var values = document.RootElement;
            double Number(string name) => values.GetProperty(name).GetDouble();
            double width = Number("w"), height = Number("h");
            if (width <= 0 || height <= 0 || Player.GetPropertyInt("width") <= 0) return null;
            // mpv reports negative margins for a zoomed video. Keep them:
            // the full displayed image size is needed for exact cursor anchoring.
            double left = Number("ml"), top = Number("mt");
            double scaleX = _videoSurface.Width / width, scaleY = _videoSurface.Height / height;
            return new RectangleF((float)(left * scaleX), (float)(top * scaleY),
                (float)((width - left - Number("mr")) * scaleX), (float)((height - top - Number("mb")) * scaleY));
        } catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    bool IsVideoViewPoint(Point point)
    {
        if (!_videoSurface.ClientRectangle.Contains(point)) return false;
        if (!_modernFullscreen) return true;
        Point hostPoint = PointToClient(_videoSurface.PointToScreen(point));
        return !(_modernHeader.Visible && _modernHeader.Bounds.Contains(hostPoint)) &&
            !(_modernFooter.Visible && _modernFooter.Bounds.Contains(hostPoint));
    }

    void ZoomVideoAtPointer(double amount, Point pointer)
    {
        if (VideoRectangle() is not { Width: > 0, Height: > 0 } rectangle) return;
        if (!Player.GetPropertyBool("keepaspect")) { ShowToast("请在视频设置中开启保持宽高比后缩放画面"); return; }
        double oldZoom = Player.GetPropertyDouble("video-zoom");
        double zoom = Math.Clamp(oldZoom + amount, -2, 4); // 25%–1600% of the fitted image
        double ratio = Math.Pow(2, zoom - oldZoom);
        double width = rectangle.Width * ratio, height = rectangle.Height * ratio;
        double targetLeft = pointer.X - (pointer.X - rectangle.Left) * ratio;
        double targetTop = pointer.Y - (pointer.Y - rectangle.Top) * ratio;
        double alignX = Player.GetPropertyDouble("video-align-x"), alignY = Player.GetPropertyDouble("video-align-y");
        if (Player.GetPropertyBool("video-recenter")) {
            if (width <= _videoSurface.Width) alignX = 0;
            if (height <= _videoSurface.Height) alignY = 0;
        }
        double baseLeft = (_videoSurface.Width - width) * (1 + alignX) / 2;
        double baseTop = (_videoSurface.Height - height) * (1 + alignY) / 2;
        Player.SetPropertyDouble("video-zoom", zoom);
        Player.SetPropertyDouble("video-pan-x", Math.Clamp((targetLeft - baseLeft) / width, -3, 3));
        Player.SetPropertyDouble("video-pan-y", Math.Clamp((targetTop - baseTop) / height, -3, 3));
        _videoViewGestureUsed = true;
        ShowOperationFeedback("video-zoom", $"画面缩放：{Math.Pow(2, zoom).ToString("0%", CultureInfo.InvariantCulture)}");
    }

    void CancelVideoPan()
    {
        if (_videoPanSuppressed) { _videoPanSuppressed = false; _videoSurface.Capture = false; }
        if (!_videoPanning) return;
        _videoPanning = false;
        _videoSurface.Capture = false;
        _videoSurface.Cursor = Cursors.Default;
    }

    void ResetVideoView(bool notify = true)
    {
        CancelVideoPan();
        Player.SetPropertyDouble("video-zoom", 0);
        Player.SetPropertyDouble("video-pan-x", 0);
        Player.SetPropertyDouble("video-pan-y", 0);
        _videoViewGestureUsed = false;
        if (notify) ShowOperationFeedback("video-zoom", "画面缩放与位置已还原");
    }
}
