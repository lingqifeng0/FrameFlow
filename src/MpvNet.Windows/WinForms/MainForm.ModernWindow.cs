using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MpvNet.Windows.Native;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    bool _modernFullscreen;
    FormWindowState _stateBeforeFullscreen;
    Rectangle _boundsBeforeFullscreen;
    int _dpiBeforeFullscreen;
    bool _modernWindowTransition;
    bool _modernInitialSizeApplied;
    readonly List<Control> _windowResizeGrips = new();
    int _windowResizeGestures;
    bool _modernInteractiveResize;
    int _resizePixelDiscards;
    System.Windows.Forms.Timer? _resizeFrameTimer;
    Rectangle? _pendingResizeBounds;
    int _resizeFrames, _resizeRepaints, _resizeRequests, _resizeCoalescedRequests;
    int _resizeNativeLayoutFailures;
    const int ModernWindowCornerRadius = 10;
    Size _roundedWindowSize;
    int _roundedWindowRadius;
    IntPtr _cornerPreferenceHandle;
    int _cornerPreference;

    void UpdateModernWindowCorners()
    {
        if (!IsHandleCreated || _modernClosing || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        bool rounded = !_modernFullscreen && WindowState == FormWindowState.Normal && !_modernWindowTransition;
        if (_modernHeader is RoundedPanel header) header.RoundedCorners = rounded;
        // Clear Windows 11's compositor rounding too: clearing the WinForms
        // region alone does not guarantee square, fully opaque monitor edges.
        int preference = rounded ? 2 /* DWMWCP_ROUND */ : 1 /* DWMWCP_DONOTROUND */;
        if (_cornerPreferenceHandle != Handle || _cornerPreference != preference) {
            WinApi.DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, new[] { preference }, sizeof(int));
            _cornerPreferenceHandle = Handle;
            _cornerPreference = preference;
        }
        int radius = rounded ? (int)Math.Round(ModernWindowCornerRadius * DeviceDpi / 96d) : 0;
        if (radius == 0) {
            _roundedWindowSize = Size.Empty;
            _roundedWindowRadius = 0;
            if (Region is { } previous) { Region = null; previous.Dispose(); }
            return;
        }
        if (_roundedWindowSize == ClientSize && _roundedWindowRadius == radius && Region != null) return;
        int diameter = Math.Min(Math.Min(ClientSize.Width, ClientSize.Height), radius * 2);
        using var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(ClientSize.Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(ClientSize.Width - diameter, ClientSize.Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, ClientSize.Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        // Cache before SetWindowRgn's native notifications can re-enter layout.
        _roundedWindowSize = ClientSize;
        _roundedWindowRadius = radius;
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    Rectangle InitialModernWindowBounds(Screen screen)
    {
        Rectangle area = screen.WorkingArea;
        int width = Math.Min(area.Width, Math.Max(MinimumSize.Width, (int)Math.Round(area.Width * .75)));
        int height = Math.Min(area.Height, Math.Max(MinimumSize.Height, (int)Math.Round(area.Height * .85)));
        return new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    void RestoreModernWindowSize()
    {
        if (_modernInitialSizeApplied) return;
        _modernInitialSizeApplied = true;
        var screen = Screen.FromControl(this);
        var initial = InitialModernWindowBounds(screen);
        if (_modernSettings.Width > 0 && _modernSettings.Height > 0) {
            double scale = _modernSettings.WindowDpi > 0 ? DeviceDpi / (double)_modernSettings.WindowDpi : 1;
            var area = screen.WorkingArea;
            int width = (int)Math.Clamp(Math.Round(_modernSettings.Width * scale), Math.Min(MinimumSize.Width, area.Width), area.Width);
            int height = (int)Math.Clamp(Math.Round(_modernSettings.Height * scale), Math.Min(MinimumSize.Height, area.Height), area.Height);
            // Center using the final restored dimensions, unless position memory is enabled.
            int left = area.Left + (area.Width-width)/2, top = area.Top + (area.Height-height)/2;
            Point position = App.Settings.WindowPosition, location = App.Settings.WindowLocation;
            if (App.RememberWindowPosition && position != Point.Empty) {
                left = location.X switch { -1 => position.X, 1 => position.X-width, _ => position.X-width/2 };
                top = location.Y switch { -1 => position.Y, 1 => position.Y-height, _ => position.Y-height/2 };
            }
            initial = new Rectangle(Math.Clamp(left, area.Left, area.Right - width), Math.Clamp(top, area.Top, area.Bottom - height), width, height);
        }
        // A maximized startup still needs the correct normal RestoreBounds.
        var state = WindowState;
        WindowState = FormWindowState.Normal;
        Bounds = initial;
        WindowState = state;
        App.Settings.WindowSize = ClientSize;
        LayoutWindowResizeGrips();
    }

    void ResetModernWindowSize()
    {
        var initial = InitialModernWindowBounds(Screen.FromControl(this));
        if (_modernFullscreen) {
            Player.SetPropertyBool("fullscreen", false);
            SetModernFullscreen(false);
        }
        WindowState = FormWindowState.Normal;
        Bounds = initial;
        RememberModernWindowSize();
        SaveModernSettings();
        PublishModernWindowState();
        ShowToast("已还原初始窗口大小");
    }

    void RememberModernWindowSize()
    {
        if (_modernSettings == null || !_modernInitialSizeApplied || _modernWindowTransition || _modernClosing) return;
        var normal = _modernFullscreen ? _boundsBeforeFullscreen : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (normal.Width <= 0 || normal.Height <= 0) return;
        _modernSettings.Width = normal.Width;
        _modernSettings.Height = normal.Height;
        _modernSettings.WindowDpi = _modernFullscreen && _dpiBeforeFullscreen > 0 ? _dpiBeforeFullscreen : DeviceDpi;
    }

    void InitializeWindowResizeGrips()
    {
        _resizeFrameTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _resizeFrameTimer.Tick += (_, _) => FlushModernResizeFrame();
        HandleCreated += (_, _) => LayoutWindowResizeGrips();
        DpiChanged += (_, _) => LayoutWindowResizeGrips();
        for (int i = 0; i < 8; i++) {
            var grip = new WindowResizeGrip(this, i) { Name = "WindowResizeGrip" + i, BackColor = Color.Black, TabStop = false };
            _windowResizeGrips.Add(grip);
            Controls.Add(grip);
            grip.BringToFront();
        }
        LayoutWindowResizeGrips();
    }

    void LayoutWindowResizeGrips()
    {
        if (_windowResizeGrips.Count != 8) return;
        bool canResize = !_modernFullscreen && WindowState == FormWindowState.Normal;
        int d = (int)Math.Ceiling(4 * DeviceDpi / 96d), w = ClientSize.Width, h = ClientSize.Height;
        // Larger corner targets remain reachable inside the rounded contour.
        // Their L-shaped input regions occupy only the edge gutter.
        int corner = Math.Max(d, (int)Math.Round(ModernWindowCornerRadius * DeviceDpi / 96d) + 2);
        // Reserve a narrow dark input margin; the grips never paint over icons.
        var padding = canResize ? new Padding(d) : Padding.Empty;
        if (Padding != padding) Padding = padding;
        Rectangle[] bounds = [new(0, corner, d, Math.Max(0, h - 2*corner)), new(w-d, corner, d, Math.Max(0, h-2*corner)),
            new(corner, 0, Math.Max(0, w-2*corner), d), new(corner, h-d, Math.Max(0, w-2*corner), d),
            new(0, 0, corner, corner), new(w-corner, 0, corner, corner), new(0, h-corner, corner, corner), new(w-corner, h-corner, corner, corner)];
        for (int i = 0; i < 8; i++) {
            _windowResizeGrips[i].Visible = canResize;
            _windowResizeGrips[i].Bounds = bounds[i];
            ((WindowResizeGrip)_windowResizeGrips[i]).ConfigureCornerMask(d);
        }
        UpdateModernWindowCorners();
    }

    void BeginModernWindowResize()
    {
        if (_modernInteractiveResize || _modernClosing || _modernFullscreen) return;
        _modernInteractiveResize = true;
        BeginModernResizePreview();
        // Keep both docked bars' space, but remove the blue paint as well as the
        // contents. Otherwise native resize pixel preservation can copy the old
        // blue title bar into the video or footer before the GPU repaints.
        _modernHeaderContent.Visible = false;
        _modernFooterContent.Visible = false;
        _modernHeaderContent.SuspendLayout();
        _modernFooterContent.SuspendLayout();
        _modernHeader.BackColor = Color.Black;
        _modernFooter.BackColor = Color.Black;
        if (_modernHeader is RoundedPanel header) header.ResizeRenderingSuspended = true;
        // Flush the plain background before the first Bounds change. Updating
        // only pending invalid regions would leave the blue rounded edge cached.
        // The outer contour stays rounded at every applied size. Only the hidden
        // header's own inner region is suspended while its blue paint is absent.
        UpdateModernWindowCorners();
        RepaintModernWindow();
        PublishModernWindowState();
    }

    void EndModernWindowResize()
    {
        _resizeFrameTimer?.Stop();
        // Mouse-up must consume the most recent pointer position even if the
        // frame timer has not yet delivered it. Capture loss uses the last move.
        FlushModernResizeFrame();
        if (!_modernInteractiveResize) return;
        if (_modernClosing || IsDisposed) { _modernInteractiveResize = false; return; }
        SuspendLayout();
        try { RestoreModernResizeSurfaces(); }
        finally { ResumeLayout(true); }
        try {
            // Hidden docked controls do not follow their parent's bounds. Apply
            // the final rectangles before revealing them, then lay out once.
            _modernHeaderContent.Bounds = _modernHeader.DisplayRectangle;
            _modernFooterContent.Bounds = _modernFooter.DisplayRectangle;
            _modernHeaderContent.ResumeLayout(true);
            _modernFooterContent.ResumeLayout(true);
            // Place the final button prefix while it is still hidden. Even a
            // synchronous paint caused by ShowWindow cannot reveal old positions.
            LayoutToolbar(true);
            _modernHeader.BackColor = ModernTheme.HeaderBackground;
            _modernFooter.BackColor = ModernTheme.Background;
            if (_modernHeader is RoundedPanel header) header.ResizeRenderingSuspended = false;
            _modernHeaderContent.Visible = true;
            _modernFooterContent.Visible = true;
        } finally {
            _modernInteractiveResize = false;
        }
        UpdateModernWindowCorners();
        RefreshModernState();
        RepaintModernWindow();
        PublishModernWindowState();
        AwaitModernResizeFrame();
    }

    void QueueModernWindowResize(Rectangle bounds)
    {
        if (_modernClosing || _modernFullscreen || WindowState != FormWindowState.Normal) return;
        if (_pendingResizeBounds == bounds || (_pendingResizeBounds == null && Bounds == bounds)) return;
        _resizeRequests++;
        if (_pendingResizeBounds != null) _resizeCoalescedRequests++;
        _pendingResizeBounds = bounds;
        BeginModernWindowResize();
        if (_resizeFrameTimer is { Enabled: false }) {
            _resizeFrameTimer.Start();
            FlushModernResizeFrame();
        }
    }

    void FlushModernResizeFrame()
    {
        var pending = _pendingResizeBounds;
        _pendingResizeBounds = null;
        if (pending == null || _modernClosing || IsDisposed || !_modernInteractiveResize) return;
        Rectangle bounds = pending.Value;
        if (Bounds == bounds) return;
        SuspendLayout();
        _modernBody.SuspendLayout();
        try { Bounds = bounds; }
        finally {
            ResumeLayout(true);
            // The GPU host gets one final rectangle, never an intermediate dock
            // rectangle while the form padding/rounded contour is updating.
            _modernBody.ResumeLayout(true);
        }
        _resizeFrames++;
        // Repaint only exposed host/chrome pixels after the final native layout.
        // mpv redraws its own surface; CPU erasure here would flash over a frame.
        RepaintModernWindow();
        foreach (Control child in new Control[] { _modernHeader, _modernFooter, _videoSurface }) {
            var expected = child.RectangleToScreen(child.ClientRectangle);
            if (WinApi.GetWindowRect(child.Handle, out var actual) && actual.ToRectangle() != expected)
                _resizeNativeLayoutFailures++;
        }
        PublishModernWindowState();
    }

    void RepaintModernWindow()
    {
        if (WindowResizeRendering.Repaint(this, _videoSurface)) _resizeRepaints++;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ModernWindowPosition
    {
        public IntPtr Window, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }

    void DiscardModernResizePixels(IntPtr pointer)
    {
        if (!_modernInteractiveResize || pointer == IntPtr.Zero || _resizePreview?.Visible == true) return;
        var position = Marshal.PtrToStructure<ModernWindowPosition>(pointer);
        // SWP_NOCOPYBITS: repaint at the new coordinates instead of moving
        // cached title-bar/footer pixels when the top or left edge changes.
        position.Flags |= 0x0100;
        Marshal.StructureToPtr(position, pointer, false);
        _resizePixelDiscards++;
    }

    // Capture real client mouse input instead of HTTRANSPARENT, which can pass
    // the pointer to the embedded video's HWND rather than to the top-level form.
    // This also avoids WS_THICKFRAME's non-client border and its gray/white paint.
    sealed class WindowResizeGrip : Control
    {
        readonly MainForm _owner;
        readonly int _edge;
        Point _origin;
        Rectangle _bounds;
        bool _dragging;
        Size _maskSize;
        int _maskGutter;

        public WindowResizeGrip(MainForm owner, int edge)
        {
            _owner = owner;
            _edge = edge;
            Cursor = edge switch {
                0 or 1 => Cursors.SizeWE, 2 or 3 => Cursors.SizeNS,
                4 or 7 => Cursors.SizeNWSE, _ => Cursors.SizeNESW
            };
        }

        public void ConfigureCornerMask(int gutter)
        {
            if (_edge < 4 || (_maskSize == Size && _maskGutter == gutter)) return;
            _maskSize = Size;
            _maskGutter = gutter;
            bool right = _edge is 5 or 7, bottom = _edge is 6 or 7;
            // A full corner square would paint over the rounded blue header.
            // Keep both resize directions available on the dark outer gutter.
            var mask = new Region(new Rectangle(0, bottom ? Height - gutter : 0, Width, gutter));
            mask.Union(new Rectangle(right ? Width - gutter : 0, 0, gutter, Height));
            var previous = Region;
            Region = mask;
            previous?.Dispose();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(1); return; } // HTCLIENT: receive the mouse ourselves.
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || _owner._modernFullscreen || _owner.WindowState != FormWindowState.Normal) return;
            _origin = PointToScreen(e.Location);
            _bounds = _owner.Bounds;
            _dragging = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || !Capture) return;
            if (_owner._modernFullscreen || _owner.WindowState != FormWindowState.Normal) { EndDrag(); return; }
            // Message coordinates also work when remote input and the desktop
            // cursor are updated at different times.
            QueuePointerPosition(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _dragging) {
                QueuePointerPosition(e.Location);
                EndDrag();
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) EndDrag();
        }

        void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            Capture = false;
            _owner.EndModernWindowResize();
            _owner._windowResizeGestures++;
            _owner.RememberModernWindowSize();
            _owner.SaveModernSettings();
            _owner.PublishModernWindowState();
        }

        void QueuePointerPosition(Point point)
        {
            // Convert before changing Bounds; all deltas stay in screen space.
            Point current = PointToScreen(point);
            _owner.QueueModernWindowResize(WindowResizeBounds.Drag(_bounds, _edge,
                current.X - _origin.X, current.Y - _origin.Y, _owner.MinimumSize));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ModernMinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    void SetModernMaximizedWorkArea(IntPtr pointer)
    {
        var screen = Screen.FromHandle(Handle);
        var info = Marshal.PtrToStructure<ModernMinMaxInfo>(pointer);
        info.MaxPosition = new Point(screen.WorkingArea.Left - screen.Bounds.Left, screen.WorkingArea.Top - screen.Bounds.Top);
        info.MaxSize = new Point(screen.WorkingArea.Width, screen.WorkingArea.Height);
        Marshal.StructureToPtr(info, pointer, false);
    }

    void SetModernFullscreen(bool enabled)
    {
        Player.Fullscreen = enabled;
        if (_modernFullscreen == enabled) return;
        EndModernWindowResize();

        Rectangle screenBounds = Screen.FromControl(this).Bounds;
        if (enabled) {
            _dpiBeforeFullscreen = DeviceDpi;
            _stateBeforeFullscreen = WindowState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
            _boundsBeforeFullscreen = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        }
        // Set the guard before changing bounds so fullscreen dimensions never
        // overwrite the saved windowed size or position in OnResize/OnMove.
        _modernFullscreen = enabled;
        _lastCycleFullscreen = Environment.TickCount;
        _modernWindowTransition = true;
        // Avoid animating a temporary restored window before the final fullscreen
        // bounds. Lay out the embedded video only after the final host size exists.
        WinApi.DwmSetWindowAttribute(Handle, 3, new[] { 1 }, 4);
        SuspendLayout();
        _modernBody.SuspendLayout();
        try {
            ConfigureFullscreenChrome(enabled);
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = enabled ? screenBounds : _boundsBeforeFullscreen;
            if (!enabled) WindowState = _stateBeforeFullscreen;
        } finally {
            ResumeLayout(true);
            ConstrainPlaylistWidth();
            _modernBody.ResumeLayout(true);
            LayoutFullscreenChrome();
            WinApi.DwmSetWindowAttribute(Handle, 3, new[] { 0 }, 4);
            _modernWindowTransition = false;
        }
        UpdateModernWindowCorners();
        PublishModernWindowState();
    }

    void HandleModernWindowMessage(string[] args)
    {
        if (args.Length < 2) return;
        switch (args[1]) {
            case "maximize":
            case "restore":
                if (_modernFullscreen) {
                    Player.SetPropertyBool("fullscreen", false);
                    SetModernFullscreen(false);
                }
                WindowState = args[1] == "maximize" ? FormWindowState.Maximized : FormWindowState.Normal;
                break;
            case "minimize": WindowState = FormWindowState.Minimized; break;
            case "fullscreen": Player.SetPropertyBool("fullscreen", true); break;
            case "exit-fullscreen": Player.SetPropertyBool("fullscreen", false); break;
            default: return;
        }
        PublishModernWindowState();
    }

    void PublishModernWindowState()
    {
        if (!WasShown || _modernClosing || _modernWindowTransition || Player.Handle == IntPtr.Zero) return;
        string mode = WindowState == FormWindowState.Minimized ? "minimized" : _modernFullscreen ? "fullscreen" :
            WindowState == FormWindowState.Maximized ? "maximized" : "normal";
        Rectangle normalBounds = _modernFullscreen ? _boundsBeforeFullscreen : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        static object Coordinates(Rectangle rect) => new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height };
        Player.SetPropertyString("user-data/modern/window", JsonSerializer.Serialize(new {
            mode, dpi = DeviceDpi, topmost = TopMost, pinChecked = _modernPin.Checked,
            cornerRadius = _roundedWindowRadius, rounded = Region != null,
            headerCornerRadius = (_modernHeader as RoundedPanel)?.CornerRadius ?? 0,
            compositorCornerPreference = _cornerPreference,
            resizing = _modernInteractiveResize, resizeGestures = _windowResizeGestures, bounds = Coordinates(Bounds), normalBounds = Coordinates(normalBounds),
            screenBounds = Coordinates(Screen.FromControl(this).Bounds),
            workArea = Coordinates(Screen.FromControl(this).WorkingArea),
            initialBounds = Coordinates(InitialModernWindowBounds(Screen.FromControl(this))),
            video = new { width = _videoSurface.Width, height = _videoSurface.Height },
            controls = new {
                autoHideFullscreen = _modernSettings.AutoHideFullscreenControls,
                hideDelaySeconds = FullscreenChromeDelay / 1000d,
                headerVisible = _modernHeader.Visible, footerVisible = _modernFooter.Visible,
                headerContentVisible = _modernHeaderContent.Visible, footerContentVisible = _modernFooterContent.Visible,
                contentHiddenForResize = _modernInteractiveResize,
                headerColor = $"#{_modernHeader.BackColor.R:X2}{_modernHeader.BackColor.G:X2}{_modernHeader.BackColor.B:X2}",
                headerRenderingSuspended = _modernHeader is RoundedPanel suspendedHeader && suspendedHeader.ResizeRenderingSuspended,
                headerRegionUpdates = _modernHeader is RoundedPanel regionHeader ? regionHeader.RegionUpdates : 0,
                resizePixelDiscards = _resizePixelDiscards,
                resizeFrames = _resizeFrames, resizeRepaints = _resizeRepaints,
                resizeRequests = _resizeRequests, resizeCoalescedRequests = _resizeCoalescedRequests,
                resizeNativeLayoutFailures = _resizeNativeLayoutFailures,
                previewVisible = _resizePreview?.Visible == true,
                previewHasFrame = _resizePreview?.HasFrame == true,
                previewRevealTimeouts = _resizeRevealTimeouts,
                previewCaptures = _resizePreviewCaptures, previewFailures = _resizePreviewFailures,
                previewPaints = _resizePreview?.PaintCount ?? 0, videoCommits = _resizeVideoCommits,
                frozenVideoWidth = _resizeFrozenVideoSize.Width, frozenVideoHeight = _resizeFrozenVideoSize.Height,
                videoWidth = _videoSurface.ClientSize.Width, videoHeight = _videoSurface.ClientSize.Height,
                headerCornerRadius = _modernHeader is RoundedPanel roundedHeader ? roundedHeader.CornerRadius : 0,
                headerButtons = _modernHeaderContent.Controls.Cast<Control>().SelectMany(control => control.Controls.OfType<ModernButton>()).Select(button => new {
                    title = button.Text, iconSize = button.IconSize, bounds = Coordinates(button.Bounds)
                }),
                headerBounds = Coordinates(_modernHeader.Bounds), footerBounds = Coordinates(_modernFooter.Bounds)
            },
            toolbar = new {
                total = _toolButtons.Count, visibleCount = _toolbarVisibleCount, overflow = _toolbarHasOverflow,
                capacity = ModernSettings.MaxToolbarItems, remainingSlots = _modernSettings.RemainingToolbarSlots,
                canAdd = _modernSettings.CanAddToolbarItem,
                layoutPasses = _toolbarLayoutPasses, width = _modernTools.ClientSize.Width, height = _modernTools.ClientSize.Height,
                showLabels = _modernSettings.ShowButtonLabels,
                buttons = _toolButtons.Select((pair, index) => new {
                    id = pair.Item.Id, title = pair.Item.Title, visible = index < _toolbarVisibleCount,
                    icon = pair.Button.Icon.ToString(),
                    bounds = Coordinates(pair.Button.Bounds), margins = pair.Button.Margin.Horizontal
                }),
                moreBounds = Coordinates(_moreButton.Bounds), moreMargins = _moreButton.Margin.Horizontal
            }
        }));
    }
}
