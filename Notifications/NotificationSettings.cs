// NotificationSettings.cs
// User-configurable alert settings, persisted as JSON in the package LocalState
// folder. The widget's Customize card writes this file; power users can also
// edit it by hand (path is logged / documented in the README).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryWidget.Notifications;

public sealed class NotificationSettings
{
    [JsonPropertyName("lowThresholdPercent")]
    public int LowThresholdPercent { get; set; } = 20;

    [JsonPropertyName("notifyLowPeripherals")]
    public bool NotifyLowPeripherals { get; set; } = true;

    [JsonPropertyName("notifyLowPc")]
    public bool NotifyLowPc { get; set; } = true;

    [JsonPropertyName("notifyFullPeripherals")]
    public bool NotifyFullPeripherals { get; set; } = true;

    // PC full: fire once on reaching 100% while charging (an "unplug" nudge),
    // never repeatedly while it sits plugged in at 100%.
    [JsonPropertyName("notifyFullPc")]
    public bool NotifyFullPc { get; set; } = true;

    // Phones keep a persistent Bluetooth link and otherwise clutter the widget;
    // hidden by default.
    [JsonPropertyName("showPhones")]
    public bool ShowPhones { get; set; } = false;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>LocalState\settings.json under the package's identity.</summary>
    public static string SettingsPath
    {
        get
        {
            try
            {
                return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "settings.json");
            }
            catch
            {
                // Unpackaged fallback (dev only).
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BatteryWidget");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.json");
            }
        }
    }

    public static NotificationSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<NotificationSettings>(json);
                if (s is not null)
                {
                    s.Clamp();
                    return s;
                }
            }
        }
        catch
        {
            // Fall through to defaults on any read/parse error.
        }
        return new NotificationSettings();
    }

    public void Save()
    {
        try
        {
            Clamp();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Best-effort; a failed save just keeps the previous settings.
        }
    }

    private void Clamp()
    {
        LowThresholdPercent = Math.Clamp(LowThresholdPercent, 5, 95);
    }
}
