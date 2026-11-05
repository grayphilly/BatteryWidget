// WidgetProvider.cs
// Implements IWidgetProvider for the Battery Status widget.

using System.Collections.Concurrent;
using System.Text.Json;
using BatteryWidget.Battery;
using BatteryWidget.Notifications;
using Microsoft.Windows.Widgets.Providers;
using Windows.System.Power;

namespace BatteryWidget;

/// <summary>Per-pinned-widget state tracked by the provider.</summary>
public sealed class CompactWidgetInfo
{
    public string WidgetId { get; init; } = "";
    public string WidgetName { get; init; } = "";
    public bool IsActive { get; set; }
    public bool InCustomization { get; set; }
}

internal sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    // Must match the Definition Id in Package.appxmanifest.
    private const string BatteryWidgetDefinitionId = "Battery_Status_Widget";

    private static readonly ConcurrentDictionary<string, CompactWidgetInfo> RunningWidgets = new();
    private static readonly ManualResetEvent EmptyWidgetListEvent = new(false);

    private static readonly Lazy<string> CardTemplate = new(LoadCardTemplate);

    // Periodic refresh while any widget is active (plan: 2-5 min; peripherals are cached by the OS).
    private readonly Timer _refreshTimer;

    public WidgetProvider()
    {
        // Restore widgets already pinned (after a restart or provider crash).
        try
        {
            foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
            {
                var ctx = info.WidgetContext;
                RunningWidgets.TryAdd(ctx.Id, new CompactWidgetInfo
                {
                    WidgetId = ctx.Id,
                    WidgetName = ctx.DefinitionId
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to restore widgets: {ex.Message}");
        }

        // Live PC battery changes -> immediate refresh.
        PowerManager.RemainingChargePercentChanged += OnPowerChanged;
        PowerManager.BatteryStatusChanged += OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged += OnPowerChanged;

        _refreshTimer = new Timer(_ => RefreshActiveWidgets(), null,
            TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
    }

    public static ManualResetEvent GetEmptyWidgetListEvent() => EmptyWidgetListEvent;

    // ---- IWidgetProvider ----

    public void CreateWidget(WidgetContext widgetContext)
    {
        var info = new CompactWidgetInfo
        {
            WidgetId = widgetContext.Id,
            WidgetName = widgetContext.DefinitionId,
            IsActive = true
        };
        RunningWidgets[widgetContext.Id] = info;
        _ = UpdateWidgetAsync(info);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        RunningWidgets.TryRemove(widgetId, out _);
        if (RunningWidgets.IsEmpty)
        {
            EmptyWidgetListEvent.Set();
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var verb = actionInvokedArgs.Verb;
        if (!RunningWidgets.TryGetValue(actionInvokedArgs.WidgetContext.Id, out var info))
        {
            return;
        }

        switch (verb)
        {
            case "refresh":
                _ = UpdateWidgetAsync(info);
                break;

            // Inline settings (host-independent: driven by a button on the card).
            case "openSettings":
                info.InCustomization = true;
                _ = UpdateWidgetAsync(info);
                break;

            case "saveSettings":
                SaveSettingsFromAction(actionInvokedArgs.Data);
                info.InCustomization = false;
                _ = UpdateWidgetAsync(info);
                break;

            case "goBack":
            case "exitCustomization":
                info.InCustomization = false;
                _ = UpdateWidgetAsync(info);
                break;
        }
    }

    // ---- IWidgetProvider2 (customization / settings) ----

    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs args)
    {
        // Note: the Windows Widgets Board on current builds does not reliably
        // invoke this. Settings are driven instead by the in-card ⚙ Settings
        // button (openSettings verb), which routes through OnActionInvoked.
        if (RunningWidgets.TryGetValue(args.WidgetContext.Id, out var info))
        {
            info.InCustomization = true;
            _ = UpdateWidgetAsync(info);
        }
    }

    private static void SaveSettingsFromAction(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            var root = doc.RootElement;
            var settings = NotificationSettings.Load();

            if (root.TryGetProperty("threshold", out var t))
            {
                // Input.Number may arrive as a JSON string or number.
                var raw = t.ValueKind == JsonValueKind.String ? t.GetString() : t.ToString();
                if (int.TryParse(raw, out int threshold))
                {
                    settings.LowThresholdPercent = threshold;
                }
            }
            settings.NotifyLowPeripherals = ReadToggle(root, "lowPeripherals", settings.NotifyLowPeripherals);
            settings.NotifyLowPc = ReadToggle(root, "lowPc", settings.NotifyLowPc);
            settings.NotifyFullPeripherals = ReadToggle(root, "fullPeripherals", settings.NotifyFullPeripherals);
            settings.NotifyFullPc = ReadToggle(root, "fullPc", settings.NotifyFullPc);
            settings.ShowPhones = ReadToggle(root, "showPhones", settings.ShowPhones);

            settings.Save();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SaveSettings failed: " + ex.Message);
        }
    }

    private static bool ReadToggle(JsonElement root, string name, bool fallback)
    {
        if (root.TryGetProperty(name, out var v))
        {
            var s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            if (bool.TryParse(s, out bool b)) return b;
        }
        return fallback;
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        if (RunningWidgets.TryGetValue(contextChangedArgs.WidgetContext.Id, out var info))
        {
            _ = UpdateWidgetAsync(info);
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        if (RunningWidgets.TryGetValue(widgetContext.Id, out var info))
        {
            info.IsActive = true;
            _ = UpdateWidgetAsync(info);
        }
    }

    public void Deactivate(string widgetId)
    {
        if (RunningWidgets.TryGetValue(widgetId, out var info))
        {
            info.IsActive = false;
        }
    }

    // ---- Updates ----

    private void OnPowerChanged(object? sender, object e) => RefreshActiveWidgets();

    private void RefreshActiveWidgets()
    {
        foreach (var info in RunningWidgets.Values)
        {
            if (info.IsActive)
            {
                _ = UpdateWidgetAsync(info);
            }
        }
    }

    private static async Task UpdateWidgetAsync(CompactWidgetInfo info)
    {
        if (info.WidgetName != BatteryWidgetDefinitionId)
        {
            return;
        }

        try
        {
            WidgetUpdateRequestOptions options;
            if (info.InCustomization)
            {
                options = new WidgetUpdateRequestOptions(info.WidgetId)
                {
                    Template = BuildSettingsCard(NotificationSettings.Load()),
                    Data = "{}",
                    IsPlaceholderContent = false
                };
            }
            else
            {
                var snapshot = await BatteryReader.GetSnapshotAsync();
                options = new WidgetUpdateRequestOptions(info.WidgetId)
                {
                    Template = CardTemplate.Value,
                    Data = BatteryReader.ToDataJson(snapshot),
                    IsPlaceholderContent = false
                };
            }
            WidgetManager.GetDefault().UpdateWidget(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"UpdateWidget failed for {info.WidgetId}: {ex.Message}");
        }
    }

    private static string LoadCardTemplate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Templates", "battery-card.json");
        return File.ReadAllText(path);
    }

    // Settings card shown in the widget's Customize flow. Values are baked in
    // from the current settings so the inputs show the saved state.
    private static string BuildSettingsCard(NotificationSettings s)
    {
        string B(bool v) => v ? "true" : "false";
        // Save/Back live in a top ActionSet so they stay visible even though the
        // inputs make the card taller than the fixed widget height (actions at
        // the card's bottom would be clipped off-screen).
        return $$"""
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.5",
          "body": [
            {
              "type": "ColumnSet",
              "columns": [
                { "type": "Column", "width": "stretch", "verticalContentAlignment": "center",
                  "items": [ { "type": "TextBlock", "text": "Battery alerts", "weight": "bolder", "size": "medium", "wrap": true } ] },
                { "type": "Column", "width": "auto", "verticalContentAlignment": "center",
                  "items": [ { "type": "ActionSet", "actions": [
                    { "type": "Action.Execute", "title": "Save", "verb": "saveSettings", "style": "positive" },
                    { "type": "Action.Execute", "title": "Back", "verb": "goBack" }
                  ] } ] }
              ]
            },
            {
              "type": "Input.Number", "id": "threshold", "label": "Low-battery threshold (%)",
              "value": {{s.LowThresholdPercent}}, "min": 5, "max": 95
            },
            { "type": "Input.Toggle", "id": "lowPeripherals", "title": "Low alert: peripherals", "value": "{{B(s.NotifyLowPeripherals)}}", "valueOn": "true", "valueOff": "false" },
            { "type": "Input.Toggle", "id": "lowPc", "title": "Low alert: PC", "value": "{{B(s.NotifyLowPc)}}", "valueOn": "true", "valueOff": "false" },
            { "type": "Input.Toggle", "id": "fullPeripherals", "title": "Full alert: peripherals", "value": "{{B(s.NotifyFullPeripherals)}}", "valueOn": "true", "valueOff": "false" },
            { "type": "Input.Toggle", "id": "fullPc", "title": "Full alert: PC", "value": "{{B(s.NotifyFullPc)}}", "valueOn": "true", "valueOff": "false" },
            { "type": "Input.Toggle", "id": "showPhones", "title": "Show phones", "value": "{{B(s.ShowPhones)}}", "valueOn": "true", "valueOff": "false" }
          ]
        }
        """;
    }
}
