using System.Text.Json;
using DXSuite.Director.Services;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Translates an Engage/DirectorClient player's <see cref="DeviceExtendedData"/> (the JSON in
/// TblDevice.ExtendedData, reported on every heartbeat) into Sentinel monitor readings + active alerts
/// and a derived health status. This is what turns the heartbeat angle from a bare online/offline signal
/// into real content-health monitoring (content status, proof-of-play freshness, JS/console errors,
/// whether the panel is actually in the foreground, screenshot freshness).
///
/// Thresholds are static defaults for now; they are the natural place to make per-descriptor/per-tenant
/// tunable later. Each non-healthy reading also emits an ActiveAlert so the evaluator's multi-symptom
/// classification (>1 alert with no Tier-1 rule -> Tier-2 AI) sees them.
/// </summary>
public static class EngageTelemetryParser
{
    public static readonly TimeSpan ScreenshotStaleWarning = TimeSpan.FromDays(7);

    /// <summary>
    /// Parse <paramref name="extendedDataJson"/> and append the resulting readings/alerts to
    /// <paramref name="state"/>. Returns the worst health implied by the telemetry, or
    /// <see cref="OverallHealthStatus.Unknown"/> when there is no parseable telemetry (caller decides the
    /// fallback — typically leave the device Healthy if it is otherwise online).
    /// </summary>
    public static OverallHealthStatus Apply(DeviceState state, string? extendedDataJson, string deviceKey, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(extendedDataJson)) return OverallHealthStatus.Unknown;

        DeviceExtendedData? ext;
        try { ext = JsonSerializer.Deserialize<DeviceExtendedData>(extendedDataJson); }
        catch (JsonException) { return OverallHealthStatus.Unknown; }
        if (ext == null) return OverallHealthStatus.Unknown;

        var worst = OverallHealthStatus.Healthy;
        var sawSignal = false;

        // Web-content health: only meaningful while the player is rendering a monitored web page
        // (an iframe whose page runs content-health.js). The applet's dead-man's-switch reports
        // healthy/unresponsive/failed; "loading"/absent means no active monitored web session
        // (e.g. the player is showing an image or video), so we skip it rather than raise false alerts.
        var contentHealth = ext.ContentHealth;
        var webStatusRaw = contentHealth?.Status?.Trim();
        var webStatus = webStatusRaw?.ToLowerInvariant();
        var webContentActive = contentHealth != null
            && !string.IsNullOrEmpty(webStatus)
            && webStatus != "loading";

        if (webContentActive)
        {
            sawSignal = true;
            var status = webStatus switch
            {
                "healthy" or "ok" => "healthy",
                "unresponsive" => "warning",
                "failed" or "critical" or "error" or "down" => "critical",
                _ => "warning"
            };
            AddReading(state, "web-content-status", "Web Content Status", webStatusRaw, status, nowUtc);
            if (status != "healthy")
            {
                worst = Worst(worst, status == "critical" ? OverallHealthStatus.Critical : OverallHealthStatus.Warning);
                AddAlert(state, deviceKey, "web-content-status", status, $"Web content health is '{webStatusRaw}'", nowUtc);
            }

            // JavaScript errors reported by the monitored page.
            if (contentHealth!.JsErrors is { } jsErrors)
            {
                var jsStatus = jsErrors > 0 ? "warning" : "healthy";
                AddReading(state, "web-content-js-errors", "Web Content JS Errors", jsErrors, jsStatus, nowUtc);
                if (jsErrors > 0)
                {
                    worst = Worst(worst, OverallHealthStatus.Warning);
                    AddAlert(state, deviceKey, "web-content-js-errors", "warning", $"{jsErrors} JavaScript error(s) in web content", nowUtc);
                }
            }

            // Time since the page last sent a health heartbeat. Informational only — the status above
            // already encodes staleness (unresponsive/failed), so no separate alert is raised here.
            if (contentHealth.LastProof is { } lastProof && lastProof.ToUniversalTime().Year > 2000)
            {
                var ageMin = (int)Math.Max(0, (nowUtc - lastProof.ToUniversalTime()).TotalMinutes);
                AddReading(state, "web-content-health-age", "Web Content Health Age", $"{ageMin} min", status, nowUtc);
            }
        }

        // Whether the player app is actually in the foreground (panel showing content, not another input).
        if (!string.IsNullOrWhiteSpace(ext.InputStatus?.Status))
        {
            sawSignal = true;
            var raw = ext.InputStatus!.Status!;
            var backgrounded = !string.Equals(raw, "foreground", StringComparison.OrdinalIgnoreCase);
            var status = backgrounded ? "warning" : "healthy";
            AddReading(state, "input-status", "Display Input", raw, status, nowUtc);
            if (backgrounded)
            {
                worst = Worst(worst, OverallHealthStatus.Warning);
                AddAlert(state, deviceKey, "input-status", "warning", "Player is not in the foreground (panel may not be showing content)", nowUtc);
            }
        }

        // Recent console errors, when console logging is enabled on the device.
        if (ext.ConsoleLogging?.Entries is { Count: > 0 } entries)
        {
            sawSignal = true;
            var errorCount = entries.Count(e => string.Equals(e.Level, "error", StringComparison.OrdinalIgnoreCase));
            var status = errorCount > 0 ? "warning" : "healthy";
            AddReading(state, "console-errors", "Console Errors", errorCount, status, nowUtc);
            if (errorCount > 0)
            {
                worst = Worst(worst, OverallHealthStatus.Warning);
                AddAlert(state, deviceKey, "console-errors", "warning", $"{errorCount} console error(s) in recent log", nowUtc);
            }
        }

        // Screenshot freshness — a stale capture hints the player has not refreshed its display.
        if (ext.Screenshot?.CapturedAt is { } captured)
        {
            sawSignal = true;
            var ageDays = (int)Math.Max(0, (nowUtc - captured.ToUniversalTime()).TotalDays);
            var status = ageDays >= ScreenshotStaleWarning.TotalDays ? "warning" : "healthy";
            AddReading(state, "screenshot-age", "Screenshot Age", $"{ageDays} d", status, nowUtc);
            if (status != "healthy")
            {
                worst = Worst(worst, OverallHealthStatus.Warning);
                AddAlert(state, deviceKey, "screenshot-age", "warning", $"Last screenshot is {ageDays} day(s) old", nowUtc);
            }
        }

        return sawSignal ? worst : OverallHealthStatus.Unknown;
    }

    private static void AddReading(DeviceState state, string id, string name, object? value, string status, DateTime nowUtc)
    {
        state.MonitorReadings[id] = new MonitorReading
        {
            MonitorPointId = id,
            Name = name,
            Value = value,
            DisplayValue = value?.ToString(),
            Status = status,
            Timestamp = nowUtc
        };
    }

    private static void AddAlert(DeviceState state, string deviceKey, string monitorPointId, string severity, string message, DateTime nowUtc)
    {
        state.ActiveAlerts.Add(new ActiveAlert
        {
            AlertId = $"{deviceKey}:{monitorPointId}",
            MonitorPointId = monitorPointId,
            Severity = severity,
            Message = message,
            TriggeredAt = nowUtc
        });
    }

    // Healthy < Warning < Critical for the purpose of this telemetry rollup (Offline/Unknown handled by the caller).
    private static OverallHealthStatus Worst(OverallHealthStatus a, OverallHealthStatus b) => Rank(b) > Rank(a) ? b : a;

    private static int Rank(OverallHealthStatus s) => s switch
    {
        OverallHealthStatus.Critical => 3,
        OverallHealthStatus.Warning => 2,
        OverallHealthStatus.Healthy => 1,
        _ => 0
    };
}
