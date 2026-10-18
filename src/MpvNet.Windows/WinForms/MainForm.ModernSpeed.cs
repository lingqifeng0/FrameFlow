using System.Globalization;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    const string ModernSpeedAudioLabel = "@mpvnet-modern-speed";

    void ConfigureModernSpeedAudio()
    {
        string filters = Player.GetPropertyString("af");
        bool hasManagedFilter = filters.Contains(ModernSpeedAudioLabel + ":", StringComparison.Ordinal);
        if (!Player.GetPropertyBool("audio-pitch-correction")) {
            if (hasManagedFilter) Player.CommandV("af", "remove", ModernSpeedAudioLabel);
            return;
        }

        // Keep explicit tempo/pitch filters under the user's control. Other
        // filters, such as an equalizer, stay in the chain when we append ours.
        if (hasManagedFilter || new[] { "scaletempo", "rubberband", "atempo", "asetrate" }
            .Any(name => filters.Contains(name, StringComparison.OrdinalIgnoreCase))) return;

        // mpv's automatically inserted scaletempo2 mutes above 8x. An explicit
        // tempo filter replaces that automatic filter and keeps audio to 16x.
        string maximum = ModernSettings.MaximumSpeed.ToString(CultureInfo.InvariantCulture);
        Player.CommandV("af", "add", ModernSpeedAudioLabel + ":scaletempo2=max-speed=" + maximum);
    }
}
