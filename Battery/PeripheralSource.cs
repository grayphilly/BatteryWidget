// PeripheralSource.cs
// Best-effort peripheral battery reading.
//
// Primary: PnpBattery reads DEVPKEY_Bluetooth_Battery directly from the PnP
//   device nodes — the same value Windows Settings shows. This covers paired
//   Bluetooth mice/keyboards/etc. that the WinRT association-endpoint selectors
//   do not expose.
//
// Devices that don't report battery to Windows (classic-BT-only headsets,
// AirPods, many controllers) are simply omitted — never shown as 0%.

namespace BatteryWidget.Battery;

public static class PeripheralSource
{
    public static Task<List<BatteryEntry>> ReadAsync()
    {
        try
        {
            return Task.FromResult(PnpBattery.GetDevices());
        }
        catch
        {
            // Any PnP/interop failure -> no peripheral rows, PC data still renders.
            return Task.FromResult(new List<BatteryEntry>());
        }
    }
}
