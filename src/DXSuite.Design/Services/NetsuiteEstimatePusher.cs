using DXSuite.Design.Entities;
using DXSuite.Design.Models;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Design.Services;

/// <summary>
/// Orchestrates pushing a worksheet to NetSuite as an Estimate.
/// Creates on first push; updates on subsequent pushes using the stored EstimateId.
/// </summary>
public class NetsuiteEstimatePusher
{
    private readonly IDbContextFactory<DesignDbContext> _dbFactory;
    private readonly INetsuiteService _netsuite;
    private readonly ITenantContext _tenant;

    public NetsuiteEstimatePusher(IDbContextFactory<DesignDbContext> dbFactory, INetsuiteService netsuite, ITenantContext tenant)
    {
        _dbFactory = dbFactory;
        _netsuite = netsuite;
        _tenant = tenant;
    }

    /// <summary>
    /// Pushes the worksheet to NetSuite and stores the returned EstimateId.
    /// Returns the estimate ID (new or existing).
    /// <paramref name="allItems"/> is the fully ordered list of line items to push,
    /// including description rows, hardware, labor, misc, and travel — assembled by the caller.
    /// </summary>
    public async Task<string> PushAsync(
        int worksheetId,
        IReadOnlyList<PushLineItem> allItems,
        EstimateConfig? config = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var worksheet = await db.TblDesignWorksheets
            .Include(w => w.Design)
            .FirstOrDefaultAsync(w => w.WorksheetId == worksheetId &&
                                      w.Design!.CustomerId == _tenant.CustomerCode, ct)
            ?? throw new InvalidOperationException($"Worksheet {worksheetId} not found.");

        var opportunityId = worksheet.Design?.NetsuiteOpportunityId ?? string.Empty;

        // Auto-resolve NS description item IDs. For each unique ItemRefName (e.g. "Labor",
        // "Miscellaneous", a Cat Group name), look up an existing NS descriptionitem by that name.
        // If none exists, create one — NS descriptionitem records only require an itemid (display name).
        // The result is cached in a name→id dict for the duration of this push.
        Dictionary<string, string> descItemIdByName = new(StringComparer.OrdinalIgnoreCase);
        var descriptionRows = allItems.Where(p => p.IsDescriptionItem).ToList();
        if (descriptionRows.Count > 0)
        {
            // Seed from existing items so we avoid creating duplicates.
            var existing = await _netsuite.GetDescriptionItemsAsync(ct);
            foreach (var item in existing)
                descItemIdByName.TryAdd(item.Name, item.Id);

            // Create any that are missing — one per unique name we actually need.
            var neededNames = descriptionRows
                .Select(p => p.ItemRefName ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var name in neededNames)
            {
                if (!descItemIdByName.ContainsKey(name))
                {
                    var newId = await _netsuite.CreateDescriptionItemAsync(name, ct);
                    descItemIdByName[name] = newId;
                }
            }
        }

        // Description items are always pushed (after resolving the item ID); regular items
        // require a non-empty NS item ID.
        var pushItems = allItems
            .Where(p => p.IsDescriptionItem || !string.IsNullOrEmpty(p.NetsuiteItemId))
            .Select(p =>
            {
                if (!p.IsDescriptionItem || p.NetsuiteItemId != "__desc__") return p;
                var resolvedId = (p.ItemRefName is not null &&
                                  descItemIdByName.TryGetValue(p.ItemRefName, out var id))
                    ? id : "";
                return p with { NetsuiteItemId = resolvedId };
            })
            .Where(p => !p.IsDescriptionItem || !string.IsNullOrEmpty(p.NetsuiteItemId))
            .ToList();

        var req = new WorksheetPushRequest(worksheetId, worksheet.NetsuiteEstimateId, opportunityId, pushItems, config);
        var estimateId = await _netsuite.PushEstimateAsync(req, ct);

        // Fetch the human-readable tranId (e.g. "QT-00123") so the toolbar can display it.
        var tranId = await _netsuite.GetEstimateTranIdAsync(estimateId, ct);

        // Persist the estimate ID and tranId — always update tranId in case it changed.
        if (string.IsNullOrEmpty(worksheet.NetsuiteEstimateId) || tranId is not null)
        {
            worksheet.NetsuiteEstimateId = estimateId;
            if (tranId is not null) worksheet.NetsuiteEstimateTranId = tranId;
            worksheet.LastEditDate = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return estimateId;
    }

}
