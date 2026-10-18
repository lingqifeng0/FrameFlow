namespace MpvNet.Windows.Modern;

public static class ResumePositionPolicy
{
    public static bool CanRestore(bool remember, bool resumePlayback, string start, double position, double duration) =>
        remember && resumePlayback && start == "none" && double.IsFinite(position) && position > 1 &&
        double.IsFinite(duration) && duration > position + 2;
}
