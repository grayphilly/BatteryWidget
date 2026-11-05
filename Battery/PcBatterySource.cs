// PcBatterySource.cs
// Reads the host PC battery via Windows.System.Power.PowerManager.

using Windows.System.Power;

namespace BatteryWidget.Battery;

public static class PcBatterySource
{
    /// <summary>
    /// Returns the PC battery entry, or null when the machine reports no battery
    /// (e.g. a desktop) so we don't show a bogus 0%.
    /// </summary>
    public static BatteryEntry? Read()
    {
        var status = PowerManager.BatteryStatus;
        if (status == BatteryStatus.NotPresent)
        {
            return null;
        }

        int percent = PowerManager.RemainingChargePercent;
        bool charging = status == BatteryStatus.Charging ||
                        PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent &&
                        status == BatteryStatus.Idle && percent >= 100;

        string statusText = status switch
        {
            BatteryStatus.Charging => percent >= 100 ? "Fully charged" : "Charging",
            BatteryStatus.Discharging => "On battery",
            BatteryStatus.Idle => PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent
                ? "Plugged in"
                : "Idle",
            _ => "Unknown"
        };

        return new BatteryEntry
        {
            Name = "This PC",
            Percent = percent,
            Charging = status == BatteryStatus.Charging,
            Status = statusText,
            Kind = BatteryKind.Pc,
            Glyph = DeviceGlyph.Pc // 💻 (charging is conveyed by status text + color)
        };
    }
}
