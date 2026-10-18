using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Windows.Forms;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    double _wheelBindingRemainder, _ctrlWheelBindingRemainder;

    bool HandleVideoWheelInput(ref Message message)
    {
        if (message.Msg != 0x020a) return false;
        long packed = message.LParam.ToInt64(), flags = message.WParam.ToInt64();
        var pointer = _videoSurface.PointToClient(new Point((short)packed, (short)(packed >> 16)));
        if (!IsVideoViewPoint(pointer)) return false;
        // Preserve Shift/Alt and horizontal-wheel bindings. MK_CONTROL supports
        // remote messages even when Windows has no physical Ctrl-key state.
        if ((ModifierKeys & (Keys.Shift | Keys.Alt)) != 0 || (flags & 0x0004) != 0) return false;
        bool control = (flags & 0x0008) != 0 || (ModifierKeys & Keys.Control) != 0;
        string action = control ? _modernSettings.CtrlWheelAction : _modernSettings.WheelAction;
        message.Result = IntPtr.Zero;
        int delta = (short)(flags >> 16);
        if (delta == 0 || action == "none") return true;
        AllowNavigationInput();
        double steps = delta / 120d;
        switch (action) {
            case "volume":
                double current = Player.GetPropertyDouble("volume");
                double volume = Math.Clamp(current + steps * 2, 0, _modernSettings.VolumeMaximum);
                Player.SetPropertyDouble("volume", volume);
                if (Math.Abs(current - volume) < .0001)
                    ShowOperationFeedback("volume", $"音量：{volume:0.#}%（{(volume == 0 ? "下限" : "上限")}）");
                break;
            case "zoom": CancelVideoPan(); ZoomVideoAtPointer(steps * .1, pointer); break;
            case "speed":
                SetSpeed((decimal)SliderDisplayValue("speed", Player.GetPropertyDouble("speed")) + (decimal)steps * _modernSettings.SpeedStep);
                break;
            case "seek":
                NavigateVideo(steps > 0 ? "前进" : "后退", "seek",
                    ((decimal)steps * _modernSettings.TapSeekSeconds).ToString(CultureInfo.InvariantCulture), "relative+exact");
                break;
            case "input-conf":
                ref double remainder = ref (control ? ref _ctrlWheelBindingRemainder : ref _wheelBindingRemainder);
                remainder += steps;
                while (Math.Abs(remainder) >= 1) {
                    int direction = Math.Sign(remainder);
                    Player.CommandV("keypress", (control ? "Ctrl+" : "") + (direction > 0 ? "WHEEL_UP" : "WHEEL_DOWN"));
                    remainder -= direction;
                }
                break;
        }
        RecordFullscreenPointer(_videoSurface.PointToScreen(pointer));
        return true;
    }

    void PublishModernMouseActions()
    {
        if (Player.Handle == IntPtr.Zero || _modernClosing) return;
        Player.SetPropertyString("user-data/modern/mouse", JsonSerializer.Serialize(new {
            wheel = _modernSettings.WheelAction, ctrlWheel = _modernSettings.CtrlWheelAction,
            ctrlDrag = _modernSettings.CtrlMouseVideoView, ctrlDragAction = _modernSettings.CtrlDragAction
        }));
    }
}
