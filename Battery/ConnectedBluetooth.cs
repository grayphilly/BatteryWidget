// ConnectedBluetooth.cs
// Set of currently-connected Bluetooth addresses via WinRT connection-status
// selectors (covers classic BT, which the CfgMgr BLE-link flag does not).
// Correlated to CfgMgr battery readings by address.

using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BatteryWidget.Battery;

public static class ConnectedBluetooth
{
    /// <summary>
    /// 12-hex-digit addresses (upper, no separators) of currently connected
    /// Bluetooth / BLE devices. Returns null if the query failed entirely, so
    /// the caller can fall back to the CfgMgr hint instead of hiding everything.
    /// </summary>
    public static async Task<HashSet<string>?> GetConnectedAddressesAsync()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] selectors =
        {
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)
        };

        bool anySucceeded = false;
        foreach (var selector in selectors)
        {
            try
            {
                var devices = await DeviceInformation.FindAllAsync(selector)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                anySucceeded = true;
                foreach (var d in devices)
                {
                    var addr = ParseAddress(d.Id);
                    if (addr is not null)
                    {
                        set.Add(addr);
                    }
                }
            }
            catch
            {
                // One selector failing/timing out is not fatal.
            }
        }

        return anySucceeded ? set : null;
    }

    // DeviceInformation.Id ends with the device address, e.g.
    // "...#BluetoothLE10:e1:8e:4f:61:d5-43:77:64:03:67:54" -> 437764036754.
    private static string? ParseAddress(string id)
    {
        var m = Regex.Match(id, "([0-9A-Fa-f]{2}(?::?[0-9A-Fa-f]{2}){5})\\s*$");
        return m.Success ? m.Groups[1].Value.Replace(":", "").ToUpperInvariant() : null;
    }
}
