// Toast.cs
// Thin wrapper over the Windows App SDK app-notification API.

using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace BatteryWidget.Notifications;

public static class Toast
{
    private static bool _registered;

    /// <summary>Register the notification manager once (required before Show).</summary>
    public static void EnsureRegistered()
    {
        if (_registered) return;
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch
        {
            // Registration can fail when run without package identity; Show
            // will then no-op rather than crash the monitor.
        }
    }

    public static void Show(string title, string body, string? glyph = null)
    {
        try
        {
            EnsureRegistered();
            if (!_registered) return;

            var builder = new AppNotificationBuilder()
                .AddText(glyph is null ? title : $"{glyph} {title}")
                .AddText(body);

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch
        {
            // Never let a notification failure take down the monitor loop.
        }
    }
}
