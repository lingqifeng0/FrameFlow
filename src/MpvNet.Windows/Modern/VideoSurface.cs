using System.Windows.Forms;

namespace MpvNet.Windows.Modern;

public delegate bool VideoInputHandler(Control source, ref Message message);

public sealed class VideoSurface : Panel
{
    public VideoInputHandler? InputHandler { get; set; }

    public VideoSurface()
    {
        // mpv disables its embedded native child. Its parent must receive input.
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        // Playback shortcuts have no text-composition target. Text entry in
        // settings/dialogs uses its own controls and keeps the normal IME.
        ImeMode = ImeMode.Disable;
    }

    protected override CreateParams CreateParams {
        get {
            var parameters = base.CreateParams;
            parameters.Style |= 0x02000000 /* WS_CLIPCHILDREN */ | 0x04000000 /* WS_CLIPSIBLINGS */;
            return parameters;
        }
    }

    // WinForms otherwise consumes arrows as dialog-focus navigation before
    // WndProc can forward key-down/key-up to mpv's held-key bindings.
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Tab || base.IsInputKey(keyData);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg is 0x0201 or 0x0204 or 0x0207 or 0x020b)
        {
            FindForm()?.Activate();
            Focus();
        }

        bool forwarded = InputHandler?.Invoke(this, ref m) == true;
        // WinForms forwards an unhandled wheel message to its parent. mpv already
        // received it, so do not apply the same wheel step a second time.
        if (forwarded && m.Msg is 0x020a or 0x020e) return;
        base.WndProc(ref m);
    }
}
