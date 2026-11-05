// PnpBattery.cs
// Reads peripheral battery levels straight from PnP device nodes via CfgMgr32 —
// the same source Windows Settings and `Get-PnpDevice` use
// (DEVPKEY_Bluetooth_Battery). Fast and reliable for paired Bluetooth devices,
// where the WinRT association-endpoint selectors do not expose the property.

using System.Runtime.InteropServices;

namespace BatteryWidget.Battery;

internal static class PnpBattery
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
        public DEVPROPKEY(string guid, uint p) { fmtid = new Guid(guid); pid = p; }
    }

    // DEVPKEY_Bluetooth_Battery = {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2 (BYTE 0-100)
    private static DEVPROPKEY PKEY_Bluetooth_Battery = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
    // DEVPKEY_Device_FriendlyName = {a45c254e-df1c-4efd-8020-67d146a850e0}, 14 (STRING)
    private static DEVPROPKEY PKEY_Device_FriendlyName = new("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
    // BLE "link connected" flag (BOOLEAN). True for actively-linked BLE devices,
    // false for disconnected ones. (May be false for connected classic BT, which
    // the WinRT connection-status path covers instead.)
    private static DEVPROPKEY PKEY_Bluetooth_LeConnected = new("83DA6326-97A6-4088-9453-A1923F573B29", 15);
    // DEVPKEY_NAME = {b725f130-47ef-101a-a5f1-02608c9eebac}, 10 (STRING) — fallback
    private static DEVPROPKEY PKEY_NAME = new("b725f130-47ef-101a-a5f1-02608c9eebac", 10);

    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
    private const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x00000001;
    private const int CR_SUCCESS = 0x00000000;
    private const int CR_BUFFER_SMALL = 0x0000001A;
    private const uint DEVPROP_TYPE_BYTE = 0x00000003;
    private const uint DEVPROP_TYPE_BOOLEAN = 0x00000011;
    private const uint DEVPROP_TYPE_STRING = 0x00000012;

    // Bluetooth enumerators whose nodes carry the battery property.
    private static readonly string[] Enumerators = { "BTHENUM", "BTHLE", "BTHLEDEVICE" };

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_SizeW(out uint pulLen, string? pszFilter, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_ListW(string? pszFilter, char[] buffer, uint bufferLen, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint dnDevInst, ref DEVPROPKEY propertyKey, out uint propertyType,
        byte[]? propertyBuffer, ref uint propertyBufferSize, uint ulFlags);

    public static List<BatteryEntry> GetDevices()
    {
        var result = new Dictionary<string, BatteryEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var enumerator in Enumerators)
        {
            foreach (var deviceId in GetDeviceIds(enumerator))
            {
                if (CM_Locate_DevNodeW(out uint devInst, deviceId, 0) != CR_SUCCESS)
                {
                    continue;
                }

                var percent = ReadByte(devInst, ref PKEY_Bluetooth_Battery);
                if (percent is null or < 0 or > 100)
                {
                    continue;
                }

                string rawName = ReadString(devInst, ref PKEY_Device_FriendlyName)
                              ?? ReadString(devInst, ref PKEY_NAME)
                              ?? "Bluetooth device";
                string name = CleanName(rawName);

                // De-dupe by cleaned name (same device can surface on multiple
                // enumerators / Bluetooth profiles); keep the lower reading.
                if (result.TryGetValue(name, out var existing) &&
                    existing.Percent <= percent)
                {
                    continue;
                }

                result[name] = new BatteryEntry
                {
                    Name = name,
                    Percent = percent,
                    Charging = false,
                    Status = "Connected",
                    Kind = BatteryKind.Peripheral,
                    Glyph = DeviceGlyph.ForDevice(rawName), // classify by device type
                    Address = ParseAddress(deviceId),
                    ConnectedHint = ReadBool(devInst, ref PKEY_Bluetooth_LeConnected) == true
                };
            }
        }

        // Lowest battery first — most actionable.
        return result.Values.OrderBy(e => e.Percent ?? 101).ToList();
    }

    // Strip the Bluetooth-profile suffixes Windows appends to classic-BT nodes
    // ("Sony WH-1000XM5 Hands-Free AG" -> "Sony WH-1000XM5").
    private static readonly string[] ProfileSuffixes =
    {
        " Hands-Free AG", " Hands-Free HF", " Hands-Free",
        " Stereo", " Avrcp Transport", " Headset", " Handsfree"
    };

    private static string CleanName(string name)
    {
        foreach (var suffix in ProfileSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
            }
        }
        return name.Trim();
    }

    private static IEnumerable<string> GetDeviceIds(string enumerator)
    {
        uint flags = CM_GETIDLIST_FILTER_PRESENT | CM_GETIDLIST_FILTER_ENUMERATOR;
        if (CM_Get_Device_ID_List_SizeW(out uint len, enumerator, flags) != CR_SUCCESS || len == 0)
        {
            yield break;
        }

        var buffer = new char[len];
        if (CM_Get_Device_ID_ListW(enumerator, buffer, len, flags) != CR_SUCCESS)
        {
            yield break;
        }

        // Multi-sz: null-separated, double-null terminated.
        foreach (var id in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return id;
        }
    }

    private static int? ReadByte(uint devInst, ref DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CM_Get_DevNode_PropertyW(devInst, ref key, out uint type, null, ref size, 0);
        if ((cr != CR_BUFFER_SMALL && cr != CR_SUCCESS) || size == 0 || type != DEVPROP_TYPE_BYTE)
        {
            return null;
        }

        var buf = new byte[size];
        if (CM_Get_DevNode_PropertyW(devInst, ref key, out type, buf, ref size, 0) != CR_SUCCESS)
        {
            return null;
        }
        return buf[0];
    }

    private static bool? ReadBool(uint devInst, ref DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CM_Get_DevNode_PropertyW(devInst, ref key, out uint type, null, ref size, 0);
        if ((cr != CR_BUFFER_SMALL && cr != CR_SUCCESS) || size == 0 || type != DEVPROP_TYPE_BOOLEAN)
        {
            return null;
        }

        var buf = new byte[size];
        if (CM_Get_DevNode_PropertyW(devInst, ref key, out type, buf, ref size, 0) != CR_SUCCESS)
        {
            return null;
        }
        return buf[0] != 0;
    }

    // Device address embedded in the PnP instance id:
    //   BTHLE\Dev_dc115127d3df\...                      -> DC115127D3DF
    //   BTHENUM\...\9&16f7503&0&F4A310E2F2BA_C00000000   -> F4A310E2F2BA
    private static string? ParseAddress(string instanceId)
    {
        var m = System.Text.RegularExpressions.Regex.Match(instanceId, "Dev_([0-9A-Fa-f]{12})");
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        m = System.Text.RegularExpressions.Regex.Match(instanceId, "&0&([0-9A-Fa-f]{12})");
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        return null;
    }

    private static string? ReadString(uint devInst, ref DEVPROPKEY key)
    {
        uint size = 0;
        int cr = CM_Get_DevNode_PropertyW(devInst, ref key, out uint type, null, ref size, 0);
        if ((cr != CR_BUFFER_SMALL && cr != CR_SUCCESS) || size == 0 || type != DEVPROP_TYPE_STRING)
        {
            return null;
        }

        var buf = new byte[size];
        if (CM_Get_DevNode_PropertyW(devInst, ref key, out type, buf, ref size, 0) != CR_SUCCESS)
        {
            return null;
        }
        // UTF-16, null-terminated.
        return System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }
}
