using System.Globalization;

namespace MpvNet.Windows.Modern;

internal static class NightShader
{
    // Twenty reusable GPU variants also work with vo=gpu, whose user shaders
    // do not support gpu-next's tunable PARAM uniforms. No CPU video filter,
    // frame copy, decoder restart, or full-window tinted overlay is needed.
    public static string EnsureFile(string configFolder, int strength)
    {
        strength = ModernSettings.NormalizeNightStrength(strength);
        string folder = Path.Combine(configFolder, "shaders", "modern-night-v1");
        string path = Path.Combine(folder, $"strength-{strength:000}.glsl");
        string amount = (strength / 100d).ToString("0.00", CultureInfo.InvariantCulture);
        string source = $$"""
            //!HOOK OUTPUT
            //!BIND HOOKED
            //!DESC FrameFlow night mode
            vec4 hook() {
                vec4 color = HOOKED_tex(HOOKED_pos);
                color.rgb *= vec3(1.0, 1.0 - 0.20 * {{amount}}, 1.0 - 0.55 * {{amount}});
                return color;
            }
            """;
        Directory.CreateDirectory(folder);
        if (!File.Exists(path) || File.ReadAllText(path) != source)
            File.WriteAllText(path, source);
        return path;
    }

    public static bool IsOwned(string configFolder, string path)
    {
        try {
            string folder = Path.GetFullPath(Path.Combine(configFolder, "shaders", "modern-night-v1"));
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), folder, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(path).StartsWith("strength-", StringComparison.Ordinal) &&
                Path.GetExtension(path).Equals(".glsl", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }
}
