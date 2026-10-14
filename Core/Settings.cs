using System.IO;
using System.Text.Json;

namespace VoicePTT.Core;

public sealed class AppSettings
{
    public string Mode { get; set; } = "Voice activity";
    public string DeviceId { get; set; } = "";
    public string LegacyDeviceName { get; set; } = "";
    public int TargetVk { get; set; } = 0x56;
    public string Hotkey { get; set; } = "Ctrl+Alt+V";
    public double ThresholdDb { get; set; } = -38;
    public double ReleaseThresholdDb { get; set; } = -44;
    public int ReleaseDelayMs { get; set; } = 300;
    public int TapWindowMs { get; set; } = 900;
    public bool LaunchAtSignIn { get; set; }
    public bool CloseToTray { get; set; } = true;
}

public static class SettingsStore
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoicePTT", "settings.json");

    public static AppSettings Load()
    {
        var result = new AppSettings();
        if (!File.Exists(FilePath)) return result;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = doc.RootElement;
            result.Mode = String(root, "mode") ?? result.Mode;
            result.DeviceId = String(root, "device_id") ?? result.DeviceId;
            result.LegacyDeviceName = String(root, "device") ?? result.LegacyDeviceName;
            result.Hotkey = String(root, "hotkey") ?? result.Hotkey;
            result.TargetVk = Integer(root, "target_vk") ?? result.TargetVk;
            if (Integer(root, "target_vk") is null && KeyCatalog.TryParse(String(root, "target_key"), out var key))
                result.TargetVk = key.Vk;
            result.ThresholdDb = Number(root, "threshold_db") ?? Number(root, "threshold") ?? result.ThresholdDb;
            result.ReleaseThresholdDb = Number(root, "release_threshold_db") ?? result.ThresholdDb - 6;
            result.ReleaseDelayMs = Integer(root, "release_delay_ms") ?? Integer(root, "release_delay") ?? result.ReleaseDelayMs;
            result.TapWindowMs = Integer(root, "tap_window_ms") ?? result.TapWindowMs;
            result.LaunchAtSignIn = Boolean(root, "launch_at_sign_in") ?? result.LaunchAtSignIn;
            result.CloseToTray = Boolean(root, "close_to_tray") ?? result.CloseToTray;
        }
        catch (Exception) { /* A damaged file should not prevent the app from opening. */ }
        result.ThresholdDb = Math.Clamp(result.ThresholdDb, -60, -15);
        result.ReleaseThresholdDb = Math.Clamp(result.ReleaseThresholdDb, -70, result.ThresholdDb);
        result.ReleaseDelayMs = Math.Clamp(result.ReleaseDelayMs, 0, 2000);
        result.TapWindowMs = Math.Clamp(result.TapWindowMs, 200, 3000);
        return result;
    }

    public static void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var values = new Dictionary<string, object>
        {
            ["mode"] = settings.Mode,
            ["device_id"] = settings.DeviceId,
            ["target_vk"] = settings.TargetVk,
            ["hotkey"] = settings.Hotkey,
            ["threshold_db"] = settings.ThresholdDb,
            ["release_threshold_db"] = settings.ReleaseThresholdDb,
            ["release_delay_ms"] = settings.ReleaseDelayMs,
            ["tap_window_ms"] = settings.TapWindowMs,
            ["launch_at_sign_in"] = settings.LaunchAtSignIn,
            ["close_to_tray"] = settings.CloseToTray
        };
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? Integer(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
    private static bool? Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False) ? value.GetBoolean() : null;
}
