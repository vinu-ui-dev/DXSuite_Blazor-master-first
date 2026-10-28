using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The "Target" a list shows for an Activation (ZONE-16): its Display Zone names, or the one display it was sent to.
/// Read from the frozen screen rows, so it names what was sent even after a Display Zone edit or a display's deletion.
/// </summary>
public static class ActivationTargetText
{
    /// <summary>
    /// Display Zone names in list order, each once; for a one-display send, that display's name as sent. Needs each row's
    /// <c>Zone</c> loaded.
    /// </summary>
    public static string Describe(IEnumerable<TblMessageActivationTarget> targets)
    {
        var rows = targets.ToList();
        var zones = rows.Where(t => t.Zone is not null).Select(t => t.Zone!)
            .DistinctBy(z => z.ZoneId)
            .OrderBy(z => z.SortOrder).ThenBy(z => z.ZoneId)
            .Select(z => z.ZoneName)
            .ToList();
        if (zones.Count > 0) return string.Join(", ", zones);

        return string.Join(", ", rows.Select(t => t.DisplayName).Distinct());
    }
}
