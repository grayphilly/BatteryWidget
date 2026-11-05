# Battery Status Widget (Windows 11)

A Windows 11 Widgets Board widget that mimics the Insider-build **Battery Status**
widget: a unified view of battery levels for your PC and **currently connected**
Bluetooth peripherals.

Built as a Windows App SDK **widget provider** (an out-of-process COM server
implementing `IWidgetProvider`) packaged as a single-project MSIX. UI is an
Adaptive Card.

## Prerequisites

- Windows 11 with the Widgets Board (Win+W).
- Developer Mode enabled.
- .NET 8 runtime (SDK builds fine with the .NET 10 SDK too).
- `winapp` (Windows App Development CLI) for dev registration, **or** Visual
  Studio 2022 as a fallback for packaging/deploy.

## Build & deploy (CLI workflow — VS Code friendly)

```bash
winapp run BatteryWidget.csproj --configuration Debug --arch x64 --no-launch
```

This builds and registers the dev package without launching it; the Widgets
Board activates the COM server when you pin the widget. Then:

1. Press **Win + W**, open **+ Add widgets**, pin **Battery Status**.
2. Resize small / medium / large from the widget's ⋯ menu.

### After code changes

Re-run the `winapp run` command above, then force the host to pick up the new
build by restarting the stale provider + board processes:

```bash
powershell -NoProfile -Command "Stop-Process -Name 'BatteryWidgetProvider','Widgets','WidgetService' -Force -ErrorAction SilentlyContinue"
```

Reopen Win+W to respawn the provider.

### If the widget isn't in the picker

Restart the Widgets service so it re-indexes providers, then reopen Win+W:

```bash
powershell -NoProfile -Command "Stop-Process -Name 'WidgetService','Widgets' -Force -ErrorAction SilentlyContinue"
```

## Project layout

| Path | Role |
|------|------|
| `Program.cs` | `CoRegisterClassObject` COM registration + process lifetime |
| `WidgetProvider.cs` | `IWidgetProvider` implementation; update timer; PowerManager events |
| `FactoryHelper.cs` | `IClassFactory` boilerplate |
| `Battery/PcBatterySource.cs` | PC battery via `Windows.System.Power.PowerManager` |
| `Battery/PnpBattery.cs` | Peripheral battery + BLE-link hint via CfgMgr32 |
| `Battery/ConnectedBluetooth.cs` | WinRT connected-device set (classic + BLE) |
| `Battery/PeripheralSource.cs` | Thin async wrapper over `PnpBattery` |
| `Battery/BatteryReader.cs` | Aggregates sources → `BatterySnapshot` JSON |
| `Battery/BatterySnapshot.cs` | Data model bound to the Adaptive Card |
| `Battery/DeviceGlyph.cs` | Maps device type → emoji glyph |
| `Notifications/NotificationSettings.cs` | User alert settings (LocalState JSON) |
| `Notifications/BatteryMonitor.cs` | Always-on alert loop (per-device state + hysteresis) |
| `Notifications/Toast.cs` | `AppNotificationManager` toast helper |
| `Templates/battery-card.json` | Adaptive Card (size-conditional, low-battery coloring) |
| `Package.appxmanifest` | comServer, widget provider, startup task; `bluetooth` capability |
| `ProviderAssets/`, `Images/` | Widget/tile icons and screenshot (placeholder art) |

## Two process roles (one exe)

The executable runs in one of two roles, chosen from its launch args in
`Program.cs`:

- **Widget provider** — when COM-activated by the Widgets host (`-Embedding`).
  Renders the Adaptive Card.
- **Battery monitor** — any other launch (no args). The package **startup task**
  launches it at logon so battery alerts work even when the Widgets Board is
  closed. Toggle it in **Task Manager › Startup apps › Battery Status Monitor**.

To start the monitor immediately without logging out:

```bash
powershell -NoProfile -Command "Start-Process 'shell:appsFolder\BatteryWidget.Provider_sq62a52at7jp8!App'"
```

## Notifications & settings

The monitor polls every 60s, compares each device to the thresholds, and raises
a toast once per crossing (with hysteresis so it doesn't nag):

- **Low battery** (peripherals and/or PC) at or below the threshold. A PC that's
  already charging is not warned.
- **Full charge** (peripherals and/or PC). PC-full fires once on reaching 100%
  while charging (an "unplug" nudge), never repeatedly while plugged in.

Settings live behind the **⚙ Settings** button on the widget card itself
(threshold number box + four on/off toggles, with Save/Back at the top). An
inline button is used instead of the ⋯ → *Customize widget* flow because the
Windows Widgets Board on current builds does not reliably call the provider's
`OnCustomizationRequested` callback — verified via logging; it fires
`Activate`/`OnWidgetContextChanged` instead. The in-card button routes through
`OnActionInvoked`, which works reliably. Settings persist to:

```
%LOCALAPPDATA%\Packages\BatteryWidget.Provider_<hash>\LocalState\settings.json
```

Power users can edit that JSON directly. The monitor reloads it every poll.
`monitor.log` and `startup.log` next to it are diagnostic logs.

## How peripheral detection works

The WinRT Bluetooth association-endpoint selectors do **not** expose device
battery. Instead `PnpBattery` reads battery level straight from PnP device-node
properties via CfgMgr32 — the same source Windows Settings uses:
`DEVPKEY_Bluetooth_Battery` `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`
(BYTE 0–100). Nodes are enumerated under the `BTHENUM`, `BTHLE`, and
`BTHLEDEVICE` enumerators, names cleaned of Bluetooth-profile suffixes
("… Hands-Free AG"), de-duplicated, and sorted lowest-battery-first.

**Connected-only filtering.** The battery node lingers with a stale value for
every device the PC has ever paired with, so we must drop disconnected ones.
No single property is reliable across transports (the BLE link flag
`{83DA6326-97A6-4088-9453-A1923F573B29} 15` is false for connected *classic*
BT, and WinRT connection status is false for *idle BLE* HID devices). So
`BatteryReader` keeps a device only if **either**:

- its BLE-link flag is set (`PnpBattery` → `ConnectedHint`), **or**
- Windows' WinRT connection-status query lists it (`ConnectedBluetooth`, covers
  classic BT), correlated by Bluetooth address.

**Phones** keep a persistent BLE link even when idle, so they'd always pass the
filter; they're excluded by device type unless the `showPhones` setting is on
(a phone may also surface under a custom Hands-Free name, which the connection
filter catches when it's disconnected).

## Known limitations

- Only devices that report battery to Windows appear. Classic-Bluetooth-only
  headsets, AirPods, and many game controllers don't, and are silently omitted
  (never shown as 0%).
- Lifetime policy is the walkthrough's simplified "run until last widget
  unpinned." Fine for personal use; a production provider would manage COM
  server lifetime independently.
- Picker icon/screenshot are placeholder art generated by
  `scratchpad/gen-assets*.ps1`.

## Debug build (console diagnostics)

Set `<OutputType>Exe</OutputType>` in the csproj to get a console window with
`Console.Error` output when the host activates the provider. Switch back to
`WinExe` for normal use (no console flash).
```
