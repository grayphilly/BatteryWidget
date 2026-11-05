// BatterySnapshot.cs
// Data shapes that feed the Adaptive Card data-binding payload.

using System.Text.Json.Serialization;

namespace BatteryWidget.Battery;

public enum BatteryKind
{
    Pc,
    Peripheral
}

public sealed class BatteryEntry
{
    /// <summary>Display name, e.g. "This PC" or "MX Master 3S".</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>0-100, or null when unknown.</summary>
    [JsonPropertyName("percent")]
    public int? Percent { get; set; }

    /// <summary>Human-readable status, e.g. "Charging", "On battery", "Full".</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    /// <summary>True when the device is currently charging / plugged in.</summary>
    [JsonPropertyName("charging")]
    public bool Charging { get; set; }

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BatteryKind Kind { get; set; }

    /// <summary>Bluetooth address (12 hex, upper, no separators). Used only to
    /// correlate with the connected set; not rendered.</summary>
    [JsonIgnore]
    public string? Address { get; set; }

    /// <summary>True when the device-node's BLE link flag says connected (fast
    /// CfgMgr signal; may be false for connected classic-BT devices).</summary>
    [JsonIgnore]
    public bool ConnectedHint { get; set; }

    /// <summary>Percent rendered for the card ("—" when unknown).</summary>
    [JsonPropertyName("percentText")]
    public string PercentText => Percent.HasValue ? $"{Percent.Value}%" : "—";

    /// <summary>true when Percent is known and below the low-battery threshold.</summary>
    [JsonPropertyName("isLow")]
    public bool IsLow => Percent.HasValue && Percent.Value <= 20 && !Charging;

    /// <summary>Glyph (Segoe Fluent / emoji) hint for the row.</summary>
    [JsonPropertyName("glyph")]
    public string Glyph { get; set; } = "";
}

public sealed class BatterySnapshot
{
    [JsonPropertyName("devices")]
    public List<BatteryEntry> Devices { get; set; } = new();

    /// <summary>Convenience: the PC entry (first Pc-kind device), if any.</summary>
    [JsonIgnore]
    public BatteryEntry? Pc => Devices.FirstOrDefault(d => d.Kind == BatteryKind.Pc);

    [JsonPropertyName("hasPeripherals")]
    public bool HasPeripherals => Devices.Any(d => d.Kind == BatteryKind.Peripheral);

    [JsonPropertyName("count")]
    public int Count => Devices.Count;

    [JsonPropertyName("updated")]
    public string Updated { get; set; } = "";
}
