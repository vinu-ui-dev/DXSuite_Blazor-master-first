using DXSuite.Connectors.Services.CoreApi.Models;

namespace DXSuite.Aviation.Services.FlightDataEditor;

/// <summary>What the Manage form shows besides its fields (FDE-05, FDE-06).</summary>
public static class FlightManageForm
{
    /// <summary>Shown when the hub changed the open flight and the form reloaded its values.</summary>
    public const string ChangedWhileOpenWarning =
        "This flight changed while the form was open. The form now shows its latest values; check them and save again.";

    /// <summary>
    /// The flight's codeshares, read-only on an existing flight: the feed's, then any an earlier Flight Edit added.
    /// </summary>
    public static IReadOnlyList<FECodeShare> CodeShares(IEnumerable<FECodeShare> feed, IEnumerable<FECodeShare>? edit)
    {
        var shares = feed.ToList();
        foreach (var share in edit ?? [])
        {
            if (!shares.Any(s => s.FLC == share.FLC && s.FLN == share.FLN)) shares.Add(share);
        }
        return shares;
    }
}
