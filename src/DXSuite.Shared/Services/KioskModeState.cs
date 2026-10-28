namespace DXSuite.Shared.Services;

/// <summary>
/// Whether the current circuit renders without DXSuite's navigation chrome — no navbar, and no
/// per-page header above whatever the page itself draws.
///
/// Two inputs, in priority order:
///
/// 1. <see cref="Override"/> — set from a "chrome" query parameter, e.g. a landing page stored as
///    "/location/5850?chrome=none". Sticky for the circuit once seen, because in-app navigation
///    drops the query string and a kiosk would otherwise sprout a navbar the first time it moved.
/// 2. <see cref="UserPreference"/> — the account's Kiosk mode setting.
///
/// This is presentation only. It hides no data and grants no access; a kiosk account still sees
/// exactly what its permissions allow, and a curious visitor can still reach any URL they could
/// have reached with the navbar visible.
/// </summary>
public class KioskModeState
{
    /// <summary>Query-string override, or null when the URL has never specified one.</summary>
    public bool? Override { get; private set; }

    /// <summary>The signed-in account's Kiosk mode preference.</summary>
    public bool UserPreference { get; private set; }

    public bool IsActive => Override ?? UserPreference;

    /// <summary>Raised when the effective value changes so the layout can re-render.</summary>
    public event Action? Changed;

    public void SetUserPreference(bool value)
    {
        if (UserPreference == value) return;

        var before = IsActive;
        UserPreference = value;
        if (IsActive != before) Changed?.Invoke();
    }

    /// <summary>
    /// Applies a "chrome" query value. Recognizes "none"/"off"/"kiosk" and "full"/"on"; anything
    /// else, including its absence, leaves the current state alone rather than resetting it.
    /// </summary>
    public void ApplyQueryValue(string? chrome)
    {
        if (string.IsNullOrWhiteSpace(chrome)) return;

        bool? requested = chrome.Trim().ToLowerInvariant() switch
        {
            "none" or "off" or "kiosk" => true,
            "full" or "on" => false,
            _ => null
        };

        if (requested == null || requested == Override) return;

        var before = IsActive;
        Override = requested;
        if (IsActive != before) Changed?.Invoke();
    }
}
