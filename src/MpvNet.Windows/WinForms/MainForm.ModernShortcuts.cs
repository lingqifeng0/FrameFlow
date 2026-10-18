using System.Text.Json;
using MpvNet.Windows.Modern;

namespace MpvNet.Windows.WinForms;

public partial class MainForm
{
    readonly Dictionary<string, string> _shortcutBase = new(StringComparer.Ordinal);
    readonly HashSet<string> _shortcutAppliedKeys = new(StringComparer.Ordinal);

    void InitializeModernShortcuts()
    {
        _shortcutBase.Clear();
        foreach (var binding in InputHelp.Parse(Player.UsedInputConfContent))
            if (binding.Input.Length > 0) _shortcutBase[binding.Input] = binding.Command.Length > 0 ? binding.Command : "ignore";
        // Migrate only the previous plain arrow defaults, preserving other
        // custom arrow bindings and modifiers, including explicitly disabled keys.
        foreach (var (key, previous, command) in new[] {
            ("Left", "seek -5", "script-binding mpvnet/modern-hold-back"),
            ("Right", "seek 5", "script-binding mpvnet/modern-hold-forward"),
            ("Up", "seek 30", "ignore"),
            ("Down", "seek -30", "ignore") }) {
            if (!_shortcutBase.TryGetValue(key, out var current) || current == previous || current == command) {
                _shortcutBase[key] = command;
                Player.CommandV("keybind", key, command);
            }
        }
        if (!_shortcutBase.ContainsKey("TAB")) {
            const string command = "script-binding mpvnet/modern-hold-temporary";
            _shortcutBase["TAB"] = command;
            Player.CommandV("keybind", "TAB", command);
        }
        ApplyModernShortcuts();
    }

    internal string[] ShortcutOriginalKeys(ShortcutAction action) => _shortcutBase
        .Where(pair => !pair.Key.Contains("MBTN", StringComparison.OrdinalIgnoreCase) &&
            !pair.Key.Contains("WHEEL", StringComparison.OrdinalIgnoreCase) && action.Matches(pair.Value))
        .Select(pair => pair.Key).ToArray();

    internal string ShortcutKey(ShortcutAction action) => _modernSettings.ShortcutOverrides.TryGetValue(action.Id, out var key)
        ? key : string.Join(" / ", ShortcutOriginalKeys(action));

    internal string? SetModernShortcut(ShortcutAction action, string key, bool restore = false)
    {
        key = key.Trim();
        if (key.Any(char.IsWhiteSpace) || key.Contains("MBTN", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("WHEEL", StringComparison.OrdinalIgnoreCase)) return "请录入键盘快捷键；鼠标操作在下方设置。";
        foreach (string candidate in restore ? ShortcutOriginalKeys(action) : new[] { key }.Where(value => value.Length > 0)) {
            foreach (var other in ShortcutCatalog.All.Where(item => item != action)) {
                var keys = _modernSettings.ShortcutOverrides.TryGetValue(other.Id, out var custom) ? new[] { custom } : ShortcutOriginalKeys(other);
                if (keys.Contains(candidate, StringComparer.Ordinal)) return $"{candidate} 已用于“{other.Title}”，请先清除原绑定。";
            }
            if (_shortcutBase.TryGetValue(candidate, out var existing) && existing != "ignore" && !action.Matches(existing) &&
                !ShortcutCatalog.All.Any(other => other.Matches(existing) && _modernSettings.ShortcutOverrides.ContainsKey(other.Id)))
                return $"{candidate} 已被其他功能使用，请换一个组合键。";
        }
        if (restore) _modernSettings.ShortcutOverrides.Remove(action.Id);
        else _modernSettings.ShortcutOverrides[action.Id] = key;
        ApplyModernShortcuts();
        SaveModernSettings();
        return null;
    }

    internal string MouseAction(string key) => _modernSettings.MouseActions.GetValueOrDefault(key, _shortcutBase.GetValueOrDefault(key, "ignore"));
    internal void SetModernMouseAction(string key, string command)
    {
        if (command == "input-conf") _modernSettings.MouseActions.Remove(key);
        else _modernSettings.MouseActions[key] = command;
        ApplyModernShortcuts();
        SaveModernSettings();
    }

    void ApplyModernShortcuts()
    {
        EndHeldNavigation(false);
        // Restore only keys managed by this page, then apply the new overrides.
        // User input.conf and unrelated/custom script bindings remain intact.
        foreach (string key in _shortcutAppliedKeys)
            Player.CommandV("keybind", key, _shortcutBase.GetValueOrDefault(key, "ignore"));
        _shortcutAppliedKeys.Clear();
        void Bind(string key, string command) {
            if (key.Length == 0) return;
            Player.CommandV("keybind", key, command);
            _shortcutAppliedKeys.Add(key);
        }
        foreach (var pair in _modernSettings.ShortcutOverrides) {
            if (ShortcutCatalog.Find(pair.Key) is not { } action) continue;
            foreach (var oldKey in ShortcutOriginalKeys(action)) Bind(oldKey, "ignore");
        }
        foreach (var pair in _modernSettings.ShortcutOverrides)
            if (ShortcutCatalog.Find(pair.Key) is { } action) Bind(pair.Value, action.Command);
        foreach (var pair in _modernSettings.MouseActions) Bind(pair.Key, pair.Value);
        Player.SetPropertyString("user-data/modern/shortcuts", JsonSerializer.Serialize(ShortcutCatalog.All.Select(action => new {
            id = action.Id, title = action.Title, category = action.Category, key = ShortcutKey(action)
        })));
    }
}
