using System.Text.Json;
using System.Text.Json.Serialization;

namespace PasteFromHistory.Core;

public sealed record AppSettings
{
    public int MenuCount { get; set; } = 10;
    public int HistoryLimit { get; set; } = 100;
    public bool SaveHistory { get; set; } = true;
    public bool CaptureImages { get; set; } = true;
    public bool CaptureFiles { get; set; } = true;
    public bool RememberSourceApp { get; set; }
    public bool CompanionMenu { get; set; } = true;
    public bool ExplorerMenu { get; set; } = true;
    public bool HotkeyEnabled { get; set; }
    public uint HotkeyModifiers { get; set; } = HotkeyGesture.Default.Modifiers;
    public uint HotkeyKey { get; set; } = HotkeyGesture.Default.Key;
    public int RetentionDays { get; set; }
    public string[] ExcludedProcesses { get; set; } = [];
    public bool StartWithWindows { get; set; }
    public bool SetupComplete { get; set; }
    [JsonIgnore] public HotkeyGesture Shortcut => new(HotkeyModifiers, HotkeyKey);

    public void Normalize()
    {
        HistoryLimit = Math.Clamp(HistoryLimit, 10, 1000);
        MenuCount = Math.Clamp(MenuCount, 1, Math.Min(30, HistoryLimit));
        RetentionDays = Math.Clamp(RetentionDays, 0, 3650);
        if (!Shortcut.IsValid)
        {
            HotkeyModifiers = HotkeyGesture.Default.Modifiers;
            HotkeyKey = HotkeyGesture.Default.Key;
            HotkeyEnabled = false;
        }
        if (CaptureExclusions.TryNormalize(ExcludedProcesses, out var names, out _)) ExcludedProcesses = names;
    }

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
        settings.Normalize();
        return settings;
    }

    public void Save(string path)
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
