// BatteryReader.cs
// Aggregates every source into a single BatterySnapshot for the widget.

using System.Text.Json;
using BatteryWidget.Notifications;

namespace BatteryWidget.Battery;

public static class BatteryReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null // property names are set explicitly via attributes
    };

    public static async Task<BatterySnapshot> GetSnapshotAsync()
    {
        var snapshot = new BatterySnapshot
        {
            Updated = DateTime.Now.ToString("t")
        };

        var pc = PcBatterySource.Read();
        if (pc is not null)
        {
            snapshot.Devices.Add(pc);
        }

        try
        {
            var peripherals = await PeripheralSource.ReadAsync();

            // Keep only devices that are actually connected right now — the PnP
            // battery node lingers with a stale value for every paired device.
            // A device counts as connected if its BLE-link flag is set OR the
            // WinRT connection-status query (which covers classic BT) lists it.
            var connected = await ConnectedBluetooth.GetConnectedAddressesAsync();
            peripherals = peripherals.Where(p =>
                p.ConnectedHint ||
                (connected is not null && p.Address is not null && connected.Contains(p.Address)))
                .ToList();

            // Phones keep a persistent link even when "not in use"; hide unless opted in.
            if (!NotificationSettings.Load().ShowPhones)
            {
                peripherals = peripherals
                    .Where(p => !DeviceGlyph.IsPhoneOrTablet(p.Glyph))
                    .ToList();
            }

            snapshot.Devices.AddRange(peripherals);
        }
        catch
        {
            // Peripheral read is best-effort; PC data still renders.
        }

        return snapshot;
    }

    /// <summary>Serialize for the Adaptive Card data payload.</summary>
    public static string ToDataJson(BatterySnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);
}
