using System.ComponentModel;
using System.Text.Json;
using DXSuite.Design.Entities;
using DXSuite.Design.Models;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace DXSuite.Design.Services.Mcp;

/// <summary>
/// Read-only MCP tool surface over the Design module. Instantiated per request (stateless HTTP
/// transport), so scoped services — most importantly the tenant-routed DbContext factory — are
/// safe to inject. Every query is scoped to the resolved tenant; ids from other tenants return
/// not-found rather than leaking existence.
/// </summary>
[McpServerToolType]
public sealed class DesignMcpTools(IDbContextFactory<DesignDbContext> dbFactory, ITenantContext tenant)
{
    private const int MaxLimit = 50;

    [McpServerTool(Name = "list_designs")]
    [Description("List designs for this tenant, newest first. Each entry includes the NetSuite opportunity number, customer, and sales rep when the design is linked to an opportunity.")]
    public async Task<object> ListDesigns(
        [Description("Optional case-insensitive filter matched against design name, customer, opportunity number, and sales rep.")] string? search = null,
        [Description("Maximum results, capped at 50.")] int limit = MaxLimit)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);

        await using var db = await dbFactory.CreateDbContextAsync();
        var designs = await db.TblDesigns.AsNoTracking()
            .Where(d => d.CustomerId == tenant.CustomerCode)
            .OrderByDescending(d => d.CreateDate)
            .ToListAsync();

        var rows = designs.Select(d =>
        {
            var opp = ParseOpportunity(d.NetsuiteOpportunityData);
            return new
            {
                designId = d.DesignId,
                name = d.Name,
                createDate = d.CreateDate,
                opportunity = opp?.TranId,
                customer = opp?.Entity,
                salesRep = string.IsNullOrEmpty(opp?.SalesRep) ? null : opp.SalesRep,
            };
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows.Where(r =>
                r.name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || r.opportunity?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
                || r.customer?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
                || r.salesRep?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        }

        var list = rows.Take(limit).ToList();
        return new
        {
            designs = list,
            truncated = list.Count == limit,
        };
    }

    [McpServerTool(Name = "get_design_requirements")]
    [Description("Get all requirements for a design, grouped by category with the same hierarchical numbering (1.1, 2.3, …) shown in the DXSuite UI and its Excel export. Categories without requirements are listed unnumbered so gaps in coverage are visible.")]
    public async Task<object> GetDesignRequirements(
        [Description("The design id, from list_designs.")] int designId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var design = await db.TblDesigns.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DesignId == designId && d.CustomerId == tenant.CustomerCode);
        if (design is null)
            return new { error = $"Design {designId} not found." };

        var categories = await db.TblDesignRequirementCategories.AsNoTracking()
            .Where(c => c.DesignId == designId)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        var requirements = await db.TblDesignRequirements.AsNoTracking()
            .Include(r => r.Comments)
            .Where(r => r.DesignId == designId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.CreateDate)
            .ToListAsync();

        var opp = ParseOpportunity(design.NetsuiteOpportunityData);

        // Same numbering rule as RequirementsPanel/RequirementsExcelExporter:
        // only categories with requirements get a number, in SortOrder position.
        var number = 0;
        var grouped = categories.Select(cat =>
        {
            var items = requirements.Where(r => r.CategoryId == cat.CategoryId).ToList();
            var catNumber = items.Count > 0 ? (int?)(++number) : null;
            return new
            {
                number = catNumber,
                name = cat.Name,
                requirements = items.Select((r, i) => new
                {
                    requirementId = r.RequirementId,
                    number = catNumber is null ? null : $"{catNumber}.{i + 1}",
                    description = r.Description,
                    priority = r.Priority.ToString(),
                    status = r.Status.ToString(),
                    clientNotes = r.ClientNotes,
                    commentCount = r.Comments.Count,
                }),
            };
        });

        return new
        {
            designId = design.DesignId,
            designName = design.Name,
            opportunity = opp?.TranId,
            customer = opp?.Entity,
            totalRequirements = requirements.Count,
            accepted = requirements.Count(r => r.Status == RequirementStatus.Accepted),
            categories = grouped,
        };
    }

    [McpServerTool(Name = "get_design")]
    [Description("Get one design's header: opportunity details, requirement categories with counts, acceptance progress, and its worksheets.")]
    public async Task<object> GetDesign(
        [Description("The design id, from list_designs.")] int designId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var design = await db.TblDesigns.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DesignId == designId && d.CustomerId == tenant.CustomerCode);
        if (design is null)
            return new { error = $"Design {designId} not found." };

        var categories = await db.TblDesignRequirementCategories.AsNoTracking()
            .Where(c => c.DesignId == designId).OrderBy(c => c.SortOrder).ToListAsync();
        var reqStats = await db.TblDesignRequirements.AsNoTracking()
            .Where(r => r.DesignId == designId)
            .GroupBy(r => r.CategoryId)
            .Select(g => new { g.Key, Count = g.Count(), Accepted = g.Count(r => r.Status == RequirementStatus.Accepted) })
            .ToListAsync();
        var worksheets = await db.TblDesignWorksheets.AsNoTracking()
            .Where(w => w.DesignId == designId).OrderBy(w => w.CreateDate)
            .Select(w => new { worksheetId = w.WorksheetId, name = w.Name, estimate = w.NetsuiteEstimateTranId })
            .ToListAsync();

        var opp = ParseOpportunity(design.NetsuiteOpportunityData);
        return new
        {
            designId = design.DesignId,
            name = design.Name,
            createDate = design.CreateDate,
            opportunity = opp is null ? null : new { number = opp.TranId, customer = opp.Entity, title = opp.Title, status = opp.EntityStatus, salesRep = opp.SalesRep },
            requirementCategories = categories.Select(c => new
            {
                name = c.Name,
                requirements = reqStats.FirstOrDefault(s => s.Key == c.CategoryId)?.Count ?? 0,
                accepted = reqStats.FirstOrDefault(s => s.Key == c.CategoryId)?.Accepted ?? 0,
            }),
            worksheets,
        };
    }

    [McpServerTool(Name = "get_requirement_comments")]
    [Description("Get the discussion thread on a single requirement.")]
    public async Task<object> GetRequirementComments(
        [Description("The requirement id, from get_design_requirements.")] int requirementId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var requirement = await db.TblDesignRequirements.AsNoTracking()
            .Include(r => r.Comments)
            .Where(r => r.RequirementId == requirementId && r.Design!.CustomerId == tenant.CustomerCode)
            .FirstOrDefaultAsync();
        if (requirement is null)
            return new { error = $"Requirement {requirementId} not found." };

        return new
        {
            requirementId,
            description = requirement.Description,
            status = requirement.Status.ToString(),
            comments = requirement.Comments.OrderBy(c => c.CreateDate).Select(c => new
            {
                author = c.AuthorName,
                date = c.CreateDate,
                body = c.Body,
            }),
        };
    }

    [McpServerTool(Name = "list_worksheets")]
    [Description("List estimate worksheets, newest first — all of them, or one design's. Use get_worksheet_summary for a worksheet's contents.")]
    public async Task<object> ListWorksheets(
        [Description("Optional design id to filter to one design.")] int? designId = null,
        [Description("Maximum results, capped at 50.")] int limit = MaxLimit)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.TblDesignWorksheets.AsNoTracking()
            .Where(w => w.Design!.CustomerId == tenant.CustomerCode);
        if (designId is not null)
            query = query.Where(w => w.DesignId == designId);

        var rows = await query
            .OrderByDescending(w => w.CreateDate)
            .Take(limit)
            .Select(w => new
            {
                worksheetId = w.WorksheetId,
                designId = w.DesignId,
                designName = w.Design!.Name,
                name = w.Name,
                estimate = w.NetsuiteEstimateTranId,
                createDate = w.CreateDate,
                rooms = w.Rooms.Count,
                lineItems = w.LineItems.Count(l => !l.IsDisabled),
            })
            .ToListAsync();

        return new { worksheets = rows, truncated = rows.Count == limit };
    }

    [McpServerTool(Name = "get_worksheet_summary")]
    [Description("Summarize one worksheet's composition for comparison: rooms, line items (with catalog item, category, and quantity), labor hours by type, and non-inventory rows. Pricing math is not recomputed here.")]
    public async Task<object> GetWorksheetSummary(
        [Description("The worksheet id, from list_worksheets or get_design.")] int worksheetId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var worksheet = await db.TblDesignWorksheets.AsNoTracking()
            .Include(w => w.Rooms)
            .Include(w => w.LineItems).ThenInclude(l => l.CatalogItem!).ThenInclude(c => c.Category)
            .Include(w => w.LaborRows).ThenInclude(l => l.LaborType)
            .Include(w => w.NonInventoryRows)
            .Include(w => w.TravelConfig)
            .Where(w => w.WorksheetId == worksheetId && w.Design!.CustomerId == tenant.CustomerCode)
            .FirstOrDefaultAsync();
        if (worksheet is null)
            return new { error = $"Worksheet {worksheetId} not found." };

        var activeItems = worksheet.LineItems.Where(l => !l.IsDisabled).ToList();
        return new
        {
            worksheetId = worksheet.WorksheetId,
            designId = worksheet.DesignId,
            name = worksheet.Name,
            estimate = worksheet.NetsuiteEstimateTranId,
            rooms = worksheet.Rooms.OrderBy(r => r.SortOrder).Select(r => new
            {
                name = r.RoomName,
                lineItems = activeItems.Count(l => l.RoomId == r.RoomId),
            }),
            lineItems = activeItems.OrderBy(l => l.SortOrder).Select(l => new
            {
                qty = l.Qty,
                description = l.CatalogItem?.Description,
                manufacturer = l.CatalogItem?.Manufacturer,
                model = l.CatalogItem?.ModelName,
                category = l.CatalogItem?.Category?.Name,
                systemName = l.SystemName,
                room = worksheet.Rooms.FirstOrDefault(r => r.RoomId == l.RoomId)?.RoomName,
            }),
            laborHours = worksheet.LaborRows
                .Select(l => new
                {
                    type = l.LaborType?.TypeName,
                    hours = l.IsOverridden ? l.OverrideHours : l.CalculatedHours,
                })
                .Where(l => l.hours > 0),
            nonInventory = worksheet.NonInventoryRows.Select(n => new { type = n.RowType.ToString(), label = n.Label }),
            hasTravelConfig = worksheet.TravelConfig is not null,
        };
    }

    [McpServerTool(Name = "find_reference_worksheets")]
    [Description("Compact summaries of recent worksheets across all designs, for ranking as references against a new design's requirements. Each entry carries the design's requirement themes and the worksheet's composition fingerprint. Compare these against the current design, then use get_worksheet_summary on the best candidates.")]
    public async Task<object> FindReferenceWorksheets(
        [Description("Optional design id to exclude (typically the design you are finding references for).")] int? excludeDesignId = null,
        [Description("Maximum worksheets returned, capped at 30.")] int limit = 30)
    {
        limit = Math.Clamp(limit, 1, 30);
        await using var db = await dbFactory.CreateDbContextAsync();

        var worksheets = await db.TblDesignWorksheets.AsNoTracking()
            .Where(w => w.Design!.CustomerId == tenant.CustomerCode
                        && (excludeDesignId == null || w.DesignId != excludeDesignId))
            .OrderByDescending(w => w.CreateDate)
            .Take(limit)
            .Select(w => new
            {
                w.WorksheetId,
                w.DesignId,
                DesignName = w.Design!.Name,
                w.Name,
                w.CreateDate,
                Rooms = w.Rooms.Count,
                Items = w.LineItems.Where(l => !l.IsDisabled).Select(l => new
                {
                    l.Qty,
                    Manufacturer = l.CatalogItem!.Manufacturer,
                    Category = l.CatalogItem!.Category!.Name,
                }).ToList(),
                LaborHours = w.LaborRows.Sum(l => l.IsOverridden ? l.OverrideHours : l.CalculatedHours),
            })
            .ToListAsync();

        var designIds = worksheets.Select(w => w.DesignId).Distinct().ToList();
        var themes = await db.TblDesignRequirements.AsNoTracking()
            .Where(r => designIds.Contains(r.DesignId))
            .GroupBy(r => new { r.DesignId, r.Category!.Name })
            .Select(g => new { g.Key.DesignId, Category = g.Key.Name, Count = g.Count() })
            .ToListAsync();

        return new
        {
            worksheets = worksheets.Select(w => new
            {
                worksheetId = w.WorksheetId,
                designId = w.DesignId,
                designName = w.DesignName,
                name = w.Name,
                createDate = w.CreateDate,
                rooms = w.Rooms,
                lineItemCount = w.Items.Sum(i => i.Qty),
                categoryMix = w.Items.Where(i => i.Category != null)
                    .GroupBy(i => i.Category).OrderByDescending(g => g.Sum(i => i.Qty))
                    .Take(5).Select(g => new { category = g.Key, qty = g.Sum(i => i.Qty) }),
                topManufacturers = w.Items.Where(i => i.Manufacturer != null)
                    .GroupBy(i => i.Manufacturer).OrderByDescending(g => g.Sum(i => i.Qty))
                    .Take(3).Select(g => g.Key),
                totalLaborHours = w.LaborHours,
                requirementThemes = themes.Where(t => t.DesignId == w.DesignId)
                    .Select(t => new { category = t.Category, count = t.Count }),
            }),
            truncated = worksheets.Count == limit,
        };
    }

    [McpServerTool(Name = "search_catalog")]
    [Description("Search the product catalog by description, manufacturer, or model.")]
    public async Task<object> SearchCatalog(
        [Description("Search term matched against description, manufacturer, and model name.")] string search,
        [Description("Maximum results, capped at 50.")] int limit = MaxLimit)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.TblDesignCatalogItems.AsNoTracking()
            .Where(i => i.CustomerId == tenant.CustomerCode
                        && (i.Description!.Contains(search)
                            || i.Manufacturer!.Contains(search)
                            || i.ModelName!.Contains(search)))
            .OrderBy(i => i.Manufacturer).ThenBy(i => i.Description)
            .Take(limit)
            .Select(i => new
            {
                itemId = i.ItemId,
                description = i.Description,
                manufacturer = i.Manufacturer,
                model = i.ModelName,
                category = i.Category!.Name,
                cost = i.Cost,
            })
            .ToListAsync();

        return new { items = rows, truncated = rows.Count == limit };
    }

    private static NetsuiteOpportunity? ParseOpportunity(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<NetsuiteOpportunity>(json); }
        catch { return null; }
    }
}
