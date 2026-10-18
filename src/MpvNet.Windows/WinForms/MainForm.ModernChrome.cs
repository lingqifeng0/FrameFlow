using System.Drawing;
using System.Windows.Forms;
using MpvNet.Windows.Native;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    const long FullscreenChromeDelay = 2500;
    long _fullscreenFooterActive;
    long _fullscreenHeaderActive;
    Point _fullscreenPointer;
    Point _fullscreenDesktopPointer;
    int _fullscreenOpenMenus;

    void TrackFullscreenChromeInput(Control control)
    {
        control.MouseMove += (_, e) => RecordFullscreenPointer(control.PointToScreen(e.Location));
        control.MouseDown += (_, e) => RecordFullscreenPointer(control.PointToScreen(e.Location));
        control.KeyDown += (_, _) => ShowFullscreenFooter();
        control.ControlAdded += (_, e) => { if (e.Control != null) TrackFullscreenChromeInput(e.Control); };
        foreach (Control child in control.Controls) TrackFullscreenChromeInput(child);
    }

    void TrackFullscreenChromeMenu(ToolStripDropDown menu)
    {
        menu.Opened += (_, _) => { _fullscreenOpenMenus++; ShowFullscreenFooter(); };
        menu.Closed += (_, _) => { _fullscreenOpenMenus = Math.Max(0, _fullscreenOpenMenus - 1); ShowFullscreenFooter(); };
    }

    void RecordFullscreenPointer(Point screenPoint)
    {
        // Remember message coordinates as well as the desktop cursor. Remote
        // input can arrive before Windows updates the physical cursor position.
        _fullscreenPointer = PointToClient(screenPoint);
        _fullscreenDesktopPointer = MousePosition;
        if (_modernFullscreen && !_modernWindowTransition) RefreshFullscreenChrome();
    }

    void ConfigureFullscreenChrome(bool fullscreen)
    {
        _fullscreenDesktopPointer = MousePosition;
        _fullscreenPointer = PointToClient(_fullscreenDesktopPointer);
        _fullscreenFooterActive = Environment.TickCount64;
        _fullscreenHeaderActive = Environment.TickCount64 - FullscreenChromeDelay;
        // Changing Anchor clears Dock in WinForms. Set anchors before docking,
        // otherwise the restored title bar silently remains an overlay.
        _modernHeader.Anchor = fullscreen ? AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Top | AnchorStyles.Left;
        _modernFooter.Anchor = fullscreen ? AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Top | AnchorStyles.Left;
        _modernHeader.Dock = fullscreen ? DockStyle.None : DockStyle.Top;
        _modernFooter.Dock = fullscreen ? DockStyle.None : DockStyle.Bottom;
        _modernHeader.Visible = !fullscreen;
        _modernFooter.Visible = true;
        if (fullscreen) {
            _modernFooter.BringToFront();
            _modernHeader.BringToFront();
        } else {
            // Docking runs in reverse z-order. Restore the original order so
            // returning to windowed mode reserves both bars above the video.
            Controls.SetChildIndex(_modernBody, 0);
            Controls.SetChildIndex(_modernFooter, 1);
            Controls.SetChildIndex(_modernHeader, 2);
            _playlistPane.Padding = Padding.Empty;
        }
    }

    void LayoutFullscreenChrome()
    {
        if (!_modernFullscreen || _modernHeader == null || _modernFooter == null) return;
        _modernHeader.SetBounds(0, 0, ClientSize.Width, _modernHeader.Height);
        _modernFooter.SetBounds(0, Math.Max(0, ClientSize.Height - _modernFooter.Height), ClientSize.Width, _modernFooter.Height);
        // The video fills the monitor behind the bars. Reserve space only in
        // the sidebar so its own controls stay usable under the overlays.
        _playlistPane.Padding = new Padding(0, _modernHeader.Height, 0, _modernFooter.Height);
    }

    void ShowFullscreenFooter()
    {
        if (!_modernFullscreen || _modernClosing) return;
        _fullscreenFooterActive = Environment.TickCount64;
        SetFullscreenChromeVisibility(_modernHeader.Visible, true);
    }

    static bool HasCapturedControl(Control control) => control.Capture || control.Controls.Cast<Control>().Any(HasCapturedControl);

    bool HasFullscreenDialog() => !Enabled || !WinApi.IsWindowEnabled(Handle) || OwnedForms.Any(form => form.Visible) ||
        (System.Windows.Application.Current?.Windows.Cast<System.Windows.Window>().Any(window => window.IsVisible &&
            new System.Windows.Interop.WindowInteropHelper(window).Owner == Handle) ?? false);

    void RefreshFullscreenChrome()
    {
        if (!_modernFullscreen || _modernWindowTransition || _modernClosing) return;
        var desktopPointer = MousePosition;
        if (desktopPointer != _fullscreenDesktopPointer) {
            _fullscreenDesktopPointer = desktopPointer;
            _fullscreenPointer = PointToClient(desktopPointer);
        }
        long now = Environment.TickCount64;
        int edge = Math.Max(8, 8 * DeviceDpi / 96);
        bool inClient = ClientRectangle.Contains(_fullscreenPointer);
        bool atFooter = inClient && _fullscreenPointer.Y >= _modernFooter.Top - edge;
        bool atHeader = inClient && _fullscreenPointer.Y < (_modernHeader.Visible ? _modernHeader.Height : edge);
        bool keepOpen = _fullscreenOpenMenus > 0 || ContextMenu.IsOpen || HasFullscreenDialog();
        bool footerBusy = _heldDirection != 0 || HasCapturedControl(_modernFooter);
        bool headerBusy = HasCapturedControl(_modernHeader);
        if (atFooter || keepOpen || footerBusy) _fullscreenFooterActive = now;
        if (atHeader || headerBusy || (keepOpen && _modernHeader.Visible)) _fullscreenHeaderActive = now;
        bool footer = !_modernSettings.AutoHideFullscreenControls || now - _fullscreenFooterActive < FullscreenChromeDelay;
        bool header = now - _fullscreenHeaderActive < FullscreenChromeDelay;
        if (_playlistResizing || _videoPanning) return;
        SetFullscreenChromeVisibility(header, footer);
    }

    void SetFullscreenChromeVisibility(bool header, bool footer)
    {
        if (_modernHeader.Visible == header && _modernFooter.Visible == footer) return;
        // DockStyle.None is essential: visibility changes must never resize
        // the embedded mpv surface, or the video would jump on every reveal.
        _modernHeader.Visible = header;
        _modernFooter.Visible = footer;
        if (header) _modernHeader.BringToFront();
        if (footer) _modernFooter.BringToFront();
        PublishModernWindowState();
    }
}
