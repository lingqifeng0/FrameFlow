using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MpvNet.Windows.Modern;

// Positions remains the only progress record. Owners are write permissions,
// not another set of timestamps: an old window cannot overwrite a newer seek.
public sealed class SharedPlaybackProgress(string settingsPath)
{
    public static string FileKey(string path)
    {
        if (path.Contains("://") || path == "-") return path;
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    static string? ExistingKey(JsonObject positions, string file)
    {
        string key = FileKey(file);
        if (positions.ContainsKey(key)) return key;
        var comparison = !key.Contains("://") && OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return positions.Select(x => x.Key).FirstOrDefault(x => FileKey(x).Equals(key, comparison));
    }
    static string OwnerKey(string file) => OperatingSystem.IsWindows() && !file.Contains("://")
        ? FileKey(file).ToUpperInvariant() : FileKey(file);

    public double? Read(string file) => SharedSettingsFile.Access(settingsPath, false, root => {
        if (root["Positions"] is not JsonObject positions) return (double?)null;
        string? key = ExistingKey(positions, file);
        return key != null && positions[key] is JsonValue value && value.TryGetValue<double>(out var number) &&
            double.IsFinite(number) && number >= 0 ? number : (double?)null;
    });

    public string Claim(string file) => SharedSettingsFile.Access(settingsPath, true, root => {
        var owners = root["ResumeOwners"] as JsonObject;
        if (owners == null) root["ResumeOwners"] = owners = new();
        string token = Guid.NewGuid().ToString("N");
        owners[OwnerKey(file)] = token;
        return token;
    });

    public bool Save(string file, string owner, double position) => SharedSettingsFile.Access(settingsPath, true, root => {
        if (!double.IsFinite(position) || position < 0 || root["ResumeOwners"] is not JsonObject owners ||
            owners[OwnerKey(file)]?.GetValue<string>() != owner) return false;
        var positions = root["Positions"] as JsonObject;
        if (positions == null) root["Positions"] = positions = new();
        string key = FileKey(file);
        string? oldKey = ExistingKey(positions, key);
        if (oldKey != null) positions.Remove(oldKey);
        positions[key] = position;
        while (positions.Count > 200) {
            string oldest = positions.First().Key;
            positions.Remove(oldest);
            owners.Remove(OwnerKey(oldest));
        }
        return true;
    });

    // Old native watch_later data is read only for migration when Positions has
    // no entry. It can never override a shared record, including an explicit 0.
    public static double? ReadLegacy(string file, IEnumerable<string> directories)
    {
        foreach (string value in new[] { file, FileKey(file) }.Distinct()) {
            string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value)));
            foreach (string directory in directories.Distinct()) {
                string cache = Path.Combine(directory, hash);
                if (!File.Exists(cache)) continue;
                foreach (string line in File.ReadLines(cache)) {
                    if (line.StartsWith("start=", StringComparison.Ordinal) && double.TryParse(line[6..],
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double position) && double.IsFinite(position) && position >= 0)
                        return position;
                }
            }
        }
        return null;
    }
}
