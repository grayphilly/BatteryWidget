// BatteryMonitor.cs
// Always-on battery watcher. Runs as a background process (launched by the
// package's startup task at logon). Polls battery levels, compares against the
// user's thresholds, and raises toasts — firing once per crossing (hysteresis),
// never nagging while a device sits at the same level.

using BatteryWidget.Battery;

namespace BatteryWidget.Notifications;

public sealed class BatteryMonitor
{
    private const int PollSeconds = 20; // responsive to device connect/disconnect
    private const int Hysteresis = 5;   // must recover this far before re-arming

    private sealed class AlertState
    {
        public bool LowNotified;
        public bool FullNotified;
    }

    private readonly Dictionary<string, AlertState> _state = new(StringComparer.OrdinalIgnoreCase);

    public async Task RunAsync(CancellationToken token)
    {
        Toast.EnsureRegistered();
        Log("monitor started");

        while (!token.IsCancellationRequested)
        {
            try
            {
                var settings = NotificationSettings.Load();
                var snapshot = await BatteryReader.GetSnapshotAsync();
                Evaluate(snapshot, settings);
            }
            catch (Exception ex)
            {
                Log("loop error: " + ex.Message);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(PollSeconds), token); }
            catch (TaskCanceledException) { break; }
        }

        Log("monitor stopped");
    }

    private void Evaluate(BatterySnapshot snapshot, NotificationSettings settings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in snapshot.Devices)
        {
            if (d.Percent is not { } pct) continue;
            seen.Add(d.Name);

            if (!_state.TryGetValue(d.Name, out var st))
            {
                st = new AlertState();
                _state[d.Name] = st;
            }

            bool isPc = d.Kind == BatteryKind.Pc;
            bool lowEnabled = isPc ? settings.NotifyLowPc : settings.NotifyLowPeripherals;
            bool fullEnabled = isPc ? settings.NotifyFullPc : settings.NotifyFullPeripherals;
            int threshold = settings.LowThresholdPercent;

            // --- Low battery ---
            // Don't warn about a PC that's already charging (it's recovering).
            bool lowCondition = lowEnabled && pct <= threshold && !(isPc && d.Charging);
            if (lowCondition && !st.LowNotified)
            {
                Toast.Show(
                    isPc ? "Low battery" : $"Charge your {d.Name}",
                    isPc ? $"Your PC is at {pct}%. Plug in to keep working."
                         : $"{d.Name} is at {pct}%. Time to charge.",
                    d.Glyph);
                st.LowNotified = true;
                Log($"low fired: {d.Name} {pct}%");
            }
            else if (st.LowNotified && (pct > threshold + Hysteresis || (isPc && d.Charging)))
            {
                st.LowNotified = false; // re-arm
            }

            // --- Full charge ---
            // PC: only while charging (so it's a genuine "reached full, unplug"),
            // fired once; peripherals: simply at 100%.
            bool fullCondition = fullEnabled && pct >= 100 && (!isPc || d.Charging);
            if (fullCondition && !st.FullNotified)
            {
                Toast.Show(
                    isPc ? "Fully charged" : $"{d.Name} fully charged",
                    isPc ? "Your PC is at 100%. You can unplug to preserve battery health."
                         : $"{d.Name} is at 100%. You can take it off the charger.",
                    d.Glyph);
                st.FullNotified = true;
                Log($"full fired: {d.Name}");
            }
            else if (st.FullNotified && (pct < 100 - Hysteresis || (isPc && !d.Charging)))
            {
                st.FullNotified = false; // re-arm
            }
        }

        // Forget devices that disconnected so they re-alert next time.
        foreach (var gone in _state.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _state.Remove(gone);
        }
    }

    private static void Log(string msg)
    {
        try
        {
            var path = Path.Combine(
                Path.GetDirectoryName(NotificationSettings.SettingsPath)!, "monitor.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
        }
        catch { /* ignore */ }
    }
}
