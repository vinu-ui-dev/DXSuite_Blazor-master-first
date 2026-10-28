namespace DXSuite.Design.Models;

public enum ScenarioMode { AllUs, OurLeads, Subs }

public record LineItemCalc(
    decimal DiscCost,
    decimal SellPrice,
    decimal ExtCost,
    decimal ExtSell);

/// <summary>
/// Full financial summary for a worksheet. All derived metrics are computed properties
/// so the WorksheetEditor only needs to pass raw totals from the calculator.
///
/// Travel costs are scenario-aware: each scenario includes only the travel cost for the
/// personnel who are on-site under that scenario. Internal-only roles always travel;
/// sub-replaceable roles may not travel in Subs/OurLeads scenarios depending on SubRole.
/// </summary>
public record WorksheetSummary(
    decimal HardwareCost,
    decimal HardwareSell,
    decimal LaborCostAllUs,
    decimal LaborSellAllUs,
    decimal LaborCostOurLeads,
    decimal LaborSellOurLeads,
    decimal LaborCostSubs,
    decimal LaborSellSubs,
    decimal MiscCost,
    decimal MiscSell,
    decimal TravelCostAllUs,
    decimal TravelSellAllUs,
    decimal TravelCostOurLeads,
    decimal TravelSellOurLeads,
    decimal TravelCostSubs,
    decimal TravelSellSubs,
    decimal MaterialsCost,
    decimal MaterialsSell)
{
    // ── Equipment (hardware catalog items + materials section combined) ───────
    // Matches the spreadsheet's SUM(M8:M294) / SUM(N8:N294) which rolls materials
    // rows into the hardware range. DXSuite tracks them separately but combines
    // them here for display so the margin calculation reflects all physical goods.
    public decimal EquipmentSell => HardwareSell + MaterialsSell;
    public decimal EquipmentCost => HardwareCost + MaterialsCost;
    public decimal EquipmentMarginDollars => EquipmentSell - EquipmentCost;
    public decimal EquipmentMarginPct => EquipmentSell == 0 ? 0
        : Math.Round(EquipmentMarginDollars / EquipmentSell * 100, 1);

    // ── Total Project = all project revenue including labor ───────────────────
    // Uses the All Us travel baseline (maximum travel scenario) so the headline
    // figure reflects the full project scope before any subcontracting offsets.
    public decimal TotalProject => HardwareSell + MaterialsSell + LaborSellAllUs + MiscSell + TravelSellAllUs;

    // ── Contribution = Labor Sell + Equipment Margin ──────────────────────────
    public decimal Contribution => LaborSellAllUs + EquipmentMarginDollars;

    // ── Labor summary (All Us as baseline reference) ─────────────────────────
    /// <summary>Labor sell at internal rates — the baseline if the company staffs entirely internally.</summary>
    public decimal LaborDollars => LaborSellAllUs;

    /// <summary>Labor sell as a percentage of total project revenue. Matches spreadsheet I3 = I2/N320.</summary>
    public decimal LaborPct => TotalProject == 0 ? 0
        : Math.Round(LaborSellAllUs / TotalProject * 100, 1);

    /// <summary>Materials sell as a percentage of total project revenue. Matches spreadsheet R2 = SUM(N288:N294)/F1.</summary>
    public decimal MaterialsPct => TotalProject == 0 ? 0
        : Math.Round(MaterialsSell / TotalProject * 100, 1);

    // ── Scenario: All Us ──────────────────────────────────────────────────────
    public decimal TotalSellAllUs => HardwareSell + MaterialsSell + LaborSellAllUs + MiscSell + TravelSellAllUs;
    public decimal TotalCostAllUs => HardwareCost + MaterialsCost + LaborCostAllUs + MiscCost + TravelCostAllUs;
    public decimal ProfitAllUs => TotalSellAllUs - TotalCostAllUs;
    public decimal ProfitPctAllUs => TotalSellAllUs == 0 ? 0
        : Math.Round(ProfitAllUs / TotalSellAllUs * 100, 1);

    // ── Scenario: Our Leads ───────────────────────────────────────────────────
    public decimal TotalSellOurLeads => HardwareSell + MaterialsSell + LaborSellOurLeads + MiscSell + TravelSellOurLeads;
    public decimal TotalCostOurLeads => HardwareCost + MaterialsCost + LaborCostOurLeads + MiscCost + TravelCostOurLeads;
    public decimal ProfitOurLeads => TotalSellOurLeads - TotalCostOurLeads;
    public decimal ProfitPctOurLeads => TotalSellOurLeads == 0 ? 0
        : Math.Round(ProfitOurLeads / TotalSellOurLeads * 100, 1);

    // ── Scenario: Subs ────────────────────────────────────────────────────────
    // Travel is partially included: Internal-only IsTravelingRole types still travel;
    // LeadSub and Sub types are replaced by local contractors and don't incur travel costs.
    public decimal TotalSellSubs => HardwareSell + MaterialsSell + LaborSellSubs + MiscSell + TravelSellSubs;
    public decimal TotalCostSubs => HardwareCost + MaterialsCost + LaborCostSubs + MiscCost + TravelCostSubs;
    public decimal ProfitSubs => TotalSellSubs - TotalCostSubs;
    public decimal ProfitPctSubs => TotalSellSubs == 0 ? 0
        : Math.Round(ProfitSubs / TotalSellSubs * 100, 1);

    // ── Overall (All Us as default) ───────────────────────────────────────────
    public decimal OverallProfit => ProfitAllUs;
    public decimal OverallProfitPct => TotalProject == 0 ? 0
        : Math.Round(OverallProfit / TotalProject * 100, 1);
}

/// <summary>Fields required by NetSuite on first estimate creation that aren't part of the worksheet data.</summary>
public record EstimateConfig(
    string LocationId,
    string BillAddressListId,
    string ShipAddressListId,
    string TermsId,
    string OpportunityTypeId,
    string LeadSourceId,
    string EntityId);

public record WorksheetPushRequest(
    int WorksheetId,
    string? EstimateId,
    string OpportunityId,
    List<PushLineItem> LineItems,
    EstimateConfig? Config = null);

public record PushLineItem(
    string NetsuiteItemId,
    decimal Qty,            // decimal to support fractional labor hours
    decimal DiscCost,
    decimal SellPrice,
    string? Description = null,         // text content; maps to NS estimate line description field
    string? LocationId = null,          // NS location internal ID; null = inherit estimate-level location
    bool PrintInPdf = false,            // maps to custcol_acs_pdf_view on the NS estimate line
    bool IsDescriptionItem = false,     // when true, use descriptionitem/{id}; omit qty/cost/rate from payload
    string? ItemRefName = null);        // for description items: sent as item.refName so NS shows the group/section name in the item column

/// <summary>A NetSuite descriptionitem catalog record, used as description-row line items in estimates.</summary>
public record NetsuiteDescriptionItem(string Id, string Name, string? Description);

/// <summary>
/// Result returned by DescriptionRowModal when the user saves a description row.
/// The NS descriptionitem ID is resolved automatically at push time — users only
/// configure the display text and the estimate location.
/// </summary>
public record DescriptionRowResult(
    string? DescriptionText,
    string LocationId,
    string LocationName);

/// <summary>Input tuple used by WorksheetCalculator.ComputeLaborHours.</summary>
public record LaborHoursInput(int CatalogItemId, int? CategoryId, int Qty);

// ── N-7: Worksheet configuration save/load ────────────────────────────────────

/// <summary>Root DTO serialized to TblDesignWorksheetConfig.ConfigJson.</summary>
public class WorksheetConfigDto
{
    public int Version { get; set; } = 1;
    public List<ConfigLineItemDto> LineItems { get; set; } = [];
    public List<ConfigLaborRowDto> LaborRows { get; set; } = [];
    public List<ConfigMiscRowDto> MiscRows { get; set; } = [];
    public List<ConfigMiscRowDto> TravelRows { get; set; } = [];
    public List<ConfigMaterialsRowDto> MaterialsRows { get; set; } = [];
    /// <summary>Maps LaborTypeId → PersonCount.</summary>
    public Dictionary<int, int> PersonCounts { get; set; } = [];
}

public class ConfigLineItemDto
{
    public int CatalogItemId { get; set; }
    public int Qty { get; set; }
    public decimal Disc { get; set; }
    public decimal Margin { get; set; }
    public string? SystemName { get; set; }
    /// <summary>Per-column hour overrides keyed by ColumnId (string key for JSON compat).</summary>
    public Dictionary<string, decimal> ColumnHours { get; set; } = [];
    public bool PrintInPdf { get; set; }
    public string? LocationId { get; set; }
    public string? LocationName { get; set; }
}

public class ConfigLaborRowDto
{
    public int LaborTypeId { get; set; }
    public bool IsOverridden { get; set; }
    public decimal OverrideHours { get; set; }
    public decimal SubCostPerHr { get; set; }
    public decimal SubPricePerHr { get; set; }
    public bool PrintInPdf { get; set; }
    public string? LocationId { get; set; }
    public string? LocationName { get; set; }
}

public class ConfigMiscRowDto
{
    public int? MiscItemId { get; set; }
    public string? NetsuiteItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Qty { get; set; }
    public decimal Cost { get; set; }
    public decimal SellPrice { get; set; }
    public bool PrintInPdf { get; set; }
    public string? LocationId { get; set; }
    public string? LocationName { get; set; }
}

public class ConfigMaterialsRowDto
{
    public int ItemId { get; set; }
    public int Qty { get; set; }
    public decimal Cost { get; set; }
    public decimal SellPrice { get; set; }
    public string? LocationId { get; set; }
    public string? LocationName { get; set; }
}

/// <summary>A catalog reference that was not found when loading a saved config.</summary>
public record WorksheetConfigMissingItem(string Section, string Description);
