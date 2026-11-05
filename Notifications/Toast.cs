// Toast.cs
// Shows toast notifications via the classic WinRT ToastNotificationManager.
// For a packaged app this works directly off the package identity — no
// AppNotificationManager.Register() (which failed here with 0x80070490) and no
// activator-CLSID manifest wiring required.

using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace BatteryWidget.Notifications;

public static class Toast
{
    public static void EnsureRegistered() { /* no-op: classic API needs no registration */ }

    public static void Show(string title, string body, string? glyph = null)
    {
        try
        {
            var headline = glyph is null ? title : $"{glyph} {title}";

            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var texts = xml.GetElementsByTagName("text");
            texts[0].AppendChild(xml.CreateTextNode(headline));
            texts[1].AppendChild(xml.CreateTextNode(body));

            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(xml));
            Log($"Show OK: {title}");
        }
        catch (Exception ex)
        {
            Log("Show FAILED: " + ex);
        }
    }

    private static void Log(string msg)
    {
        try
        {
            var path = Path.Combine(
                Path.GetDirectoryName(NotificationSettings.SettingsPath)!, "toast.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }
}
