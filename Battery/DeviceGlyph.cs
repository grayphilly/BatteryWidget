// DeviceGlyph.cs
// Maps a device to an emoji glyph by type. Emoji render in Adaptive Cards via
// Segoe UI Emoji and need no packaged image assets. Classification is keyword-
// based on the device's reported name (including Bluetooth profile hints such
// as "Hands-Free", which indicate an audio device).

namespace BatteryWidget.Battery;

public static class DeviceGlyph
{
    public const string Pc = "\U0001F4BB";          // 💻 laptop
    public const string Desktop = "\U0001F5A5️"; // 🖥️
    public const string Headphones = "\U0001F3A7";   // 🎧
    public const string Earbuds = "\U0001FAE7";      // 🫧 (buds) -> fallback to headphones below
    public const string Speaker = "\U0001F50A";      // 🔊
    public const string Keyboard = "⌨️";   // ⌨️
    public const string Mouse = "\U0001F5B1️";  // 🖱️
    public const string Phone = "\U0001F4F1";        // 📱
    public const string Tablet = "\U0001F4F1";       // 📱 (no distinct tablet emoji)
    public const string Watch = "⌚";            // ⌚
    public const string Controller = "\U0001F3AE";   // 🎮
    public const string Pen = "\U0001F58A️";    // 🖊️
    public const string Generic = "\U0001F50B";      // 🔋

    private static readonly (string[] keywords, string glyph)[] Rules =
    {
        // Audio: speakers first (more specific brands), then headphones/earbuds.
        (new[] { "speaker", "soundbar", "sound bar", "srs-", "srs ", "xb100", "jbl",
                 "boombox", "flip ", "charge ", "echo", "sonos", "homepod", "soundcore motion" }, Speaker),
        (new[] { "headphone", "headphones", "headset", "earphone", "earbud", "earbuds",
                 "buds", "airpod", "wh-", "wf-", "freebud", "quietcomfort", "qc35", "qc45",
                 "momentum", "hands-free", "handsfree", "stereo", "a2dp" }, Headphones),

        // Input devices.
        (new[] { "keyboard", "keychron", "mx keys", "keeb", "das keyboard", "ergodox" }, Keyboard),
        (new[] { "mouse", "mx master", "mx ergo", "mx anywhere", "trackball",
                 "trackpad", "magic mouse", "magic trackpad" }, Mouse),
        (new[] { "pen", "stylus", "slim pen", "surface pen" }, Pen),

        // Mobile / wearable.
        (new[] { "iphone", "pixel", "galaxy s", "galaxy z", "oneplus", "xperia",
                 "moto g", "phone" }, Phone),
        (new[] { "ipad", "galaxy tab", "surface pro", "tablet" }, Tablet),
        (new[] { "watch", "band", "fitbit", "garmin", "amazfit", "versa", "charge hr" }, Watch),

        // Gaming.
        (new[] { "controller", "gamepad", "xbox", "dualsense", "dualshock",
                 "joy-con", "joycon", "stadia", "8bitdo" }, Controller),
    };

    /// <summary>True when the device classifies as a phone or tablet.</summary>
    public static bool IsPhoneOrTablet(string glyph) => glyph == Phone || glyph == Tablet;

    /// <summary>Glyph for a peripheral, from its (raw) device name.</summary>
    public static string ForDevice(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var (keywords, glyph) in Rules)
        {
            foreach (var kw in keywords)
            {
                if (lower.Contains(kw))
                {
                    return glyph;
                }
            }
        }
        return Generic;
    }
}
