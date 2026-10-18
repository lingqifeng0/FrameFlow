
using System.Reflection;

namespace MpvNet;

public static class AppInfo
{
    public const string Name = "FrameFlow";
    public static string FrontendConfigPath(string directory)
    {
        string current = Path.Combine(directory, "frameflow.conf");
        string legacy = Path.Combine(directory, "mpvnet.conf");
        if (!File.Exists(current) && File.Exists(legacy)) {
            try { File.Copy(legacy, current, false); }
            catch (IOException) when (File.Exists(current)) { } // Another instance migrated first.
            catch (IOException) { return legacy; }
            catch (UnauthorizedAccessException) { return legacy; }
        }
        return current;
    }
    public static string Product => GetAssemblyAttribute<AssemblyProductAttribute>().Product;
    public static Version Version => Assembly.GetEntryAssembly()!.GetName().Version!;
    public static string ReleaseLabel => Version.Revision >= 1000 ? "V" + (Version.Revision - 1000) : "开发构建";
    static T GetAssemblyAttribute<T>() => (T)(object)Assembly.GetEntryAssembly()!.GetCustomAttributes(typeof(T)).First();
}
