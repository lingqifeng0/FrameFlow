using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MpvNet.Windows.Modern;

internal static class WindowResizeRendering
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RedrawWindow(IntPtr window, IntPtr rectangle, IntPtr region, uint flags);

    // GDI owns only the host/chrome. Never propagate erase/invalidate into the
    // video subtree: its separate GPU swap chain owns both video and black bars.
    // WS_CLIPCHILDREN keeps each parent's background out of its child windows.
    public static bool Repaint(Control window, Control? video = null)
    {
        if (!window.IsHandleCreated || window.IsDisposed) return false;
        if (window == video) return true;
        const uint invalidate = 0x0001, erase = 0x0004, noChildren = 0x0040,
            updateNow = 0x0100, frame = 0x0400;
        bool painted = RedrawWindow(window.Handle, IntPtr.Zero, IntPtr.Zero,
            invalidate | erase | noChildren | updateNow | frame);
        foreach (Control child in window.Controls)
            if (child.Visible && child != video) painted &= Repaint(child, video);
        return painted;
    }
}
