using DXSuite.Design.Entities;
using DXSuite.Design.Models;

namespace DXSuite.Design.Services;

/// <summary>
/// Pure calculation service. All methods are stateless and take explicit inputs
/// so they can be unit-tested without a database.
/// </summary>
public static class WorksheetCalculator
{
    public static LineItemCalc ComputeLineItem(decimal cost, decimal discountPct, decimal marginPct, int qty)
    {
        var discCost = cost * (1 - discountPct);
        var sellPrice = marginPct >= 1 ? discCost
            : Math.Round(discCost / (1 - marginPct), 0, MidpointRounding.AwayFromZero);
        return new LineItemCalc(discCost, sellPrice, discCost * qty, sellPrice * qty);
    }

    /// <summary>
    /// Three-pass labor hour computation using a per-column proportional staff-count model.
    ///
    /// Pass 1 — Column-based types: for each column, sum raw hours across all items, then split
    ///           those hours proportionally among the labor types assigned to THAT column whose
    ///           staff count is > 0. Each labor type's total = sum of its shares across all its
    ///           assigned columns. A labor type with PersonCount = 0 is excluded entirely.
    ///
    ///           Example: Cabling (6 hrs) and Installation (12 hrs) are both assigned to Lead Tech
    ///           (1 staff) and Tech (2 staff). Each column is split 1:2 between them independently:
    ///           Lead gets 2+4 = 6 hrs, Tech gets 4+8 = 12 hrs.
    ///           A separate Commissioning column (4 hrs) assigned only to Lead Commissioning (1 staff)
    ///           gives that type all 4 hrs; setting its staff to 0 gives it 0 hrs.
    ///
    /// Pass 2 — Percentage-based types: hours = totalRawColumnHours × ColumnPercentage / 100,
    ///           where totalRawColumnHours is the physical column total before staff allocation.
    /// Pass 3 — Manual types (no column assignments, not percentage): result = 0;
    ///           hours are driven solely by the user's EstimatedHours override.
    /// </summary>
    public static Dictionary<int, decimal> ComputeLaborHours(
        IReadOnlyList<LaborHoursInput> lineItems,
        ILookup<int, TblDesignCategoryColumnAssignment> catColumnLookup,
        ILookup<int, TblDesignCatalogItemColumn> itemColumnLookup,
        IReadOnlyDictionary<int, int> personCountsByLaborType,  // LaborTypeId → PersonCount (0 = excluded)
        IReadOnlyList<TblDesignLaborType> laborTypes,
        ILookup<int, int> laborTypeColumnMap)  // LaborTypeId → ColumnIds
    {
        var result = new Dictionary<int, decimal>();

        // All column IDs referenced by any non-percentage, non-manual labor type
        var allUsedColumnIds = laborTypes
            .Where(l => !l.IsPercentageOfColumns && laborTypeColumnMap[l.LaborTypeId].Any())
            .SelectMany(l => laborTypeColumnMap[l.LaborTypeId])
            .Distinct()
            .ToList();

        // Step A: compute raw hours per column (sum across all line items)
        var columnHours = new Dictionary<int, decimal>(allUsedColumnIds.Count);
        foreach (var colId in allUsedColumnIds)
        {
            decimal total = 0;
            foreach (var li in lineItems)
            {
                var h = itemColumnLookup[li.CatalogItemId]
                    .FirstOrDefault(o => o.ColumnId == colId)?.Hours
                    ?? (li.CategoryId.HasValue
                        ? catColumnLookup[li.CategoryId.Value]
                            .FirstOrDefault(a => a.ColumnId == colId)?.DefaultHours
                        : null)
                    ?? 0;
                total += h * li.Qty;
            }
            columnHours[colId] = total;
        }

        // Step B: for each column, build the list of participating labor types (PersonCount > 0)
        // and their total staff count — used as the denominator for each column's split.
        var columnTotalPersons = new Dictionary<int, int>(allUsedColumnIds.Count);
        foreach (var colId in allUsedColumnIds)
        {
            int total = 0;
            foreach (var lt in laborTypes.Where(l => !l.IsPercentageOfColumns))
            {
                if (!laborTypeColumnMap[lt.LaborTypeId].Contains(colId)) continue;
                var pc = personCountsByLaborType.TryGetValue(lt.LaborTypeId, out var c) ? c : 1;
                if (pc > 0) total += pc;
            }
            columnTotalPersons[colId] = total;
        }

        // Pass 1: accumulate each labor type's proportional share from each of its columns
        decimal totalRawColumnHours = 0;
        foreach (var lt in laborTypes.Where(l => !l.IsPercentageOfColumns))
        {
            var colIds = laborTypeColumnMap[lt.LaborTypeId].ToList();
            if (colIds.Count == 0)
            {
                result[lt.LaborTypeId] = 0; // manual — handled in Pass 3
                continue;
            }

            var pc = personCountsByLaborType.TryGetValue(lt.LaborTypeId, out var c) ? c : 1;
            if (pc <= 0)
            {
                result[lt.LaborTypeId] = 0; // zero staff = excluded from all column pools
                continue;
            }

            decimal ltHours = 0;
            foreach (var colId in colIds)
            {
                var colRaw = columnHours.GetValueOrDefault(colId);
                if (colRaw == 0) continue;
                var denominator = columnTotalPersons.GetValueOrDefault(colId);
                if (denominator > 0)
                    ltHours += colRaw * pc / denominator;
            }

            // Enforce minimum hours: when staff is assigned and the calculation falls below the
            // configured minimum, use the minimum instead. This ensures a floor for roles like
            // installation that NetSuite requires to be at least N hours regardless of item count.
            if (lt.MinimumHours.HasValue && lt.MinimumHours.Value > 0 && ltHours < lt.MinimumHours.Value)
                ltHours = lt.MinimumHours.Value;

            result[lt.LaborTypeId] = Math.Round(ltHours, 2);
        }

        // totalRawColumnHours = sum of physical column hours (before staff allocation),
        // used as the base for percentage types so they reflect actual scope regardless of staffing.
        totalRawColumnHours = columnHours.Values.Sum();

        // Pass 2: percentage-based types derive from total raw column hours
        foreach (var lt in laborTypes.Where(l => l.IsPercentageOfColumns))
            result[lt.LaborTypeId] = Math.Round(totalRawColumnHours * (lt.ColumnPercentage ?? 0) / 100, 2);

        // Pass 3: manual types (no column assignments, not percentage) — result = 0
        foreach (var lt in laborTypes.Where(l => !l.IsPercentageOfColumns && !laborTypeColumnMap[l.LaborTypeId].Any()))
            result[lt.LaborTypeId] = 0;

        return result;
    }

    /// <summary>
    /// Computes per-unit per-column hours for a single line item (for display in the hardware table).
    /// Returns per-unit values — callers must multiply by qty for totals.
    /// Item-level overrides replace category defaults for the same column.
    /// </summary>
    public static Dictionary<int, decimal> ComputeItemColumnHours(
        int catalogItemId,
        int? categoryId,
        ILookup<int, TblDesignCategoryColumnAssignment> catColumnLookup,
        ILookup<int, TblDesignCatalogItemColumn> itemColumnLookup,
        IReadOnlyList<int> columnIds)
    {
        var result = new Dictionary<int, decimal>();
        foreach (var colId in columnIds)
        {
            var hours = itemColumnLookup[catalogItemId].FirstOrDefault(o => o.ColumnId == colId)?.Hours
                ?? (categoryId.HasValue
                    ? catColumnLookup[categoryId.Value].FirstOrDefault(a => a.ColumnId == colId)?.DefaultHours
                    : null)
                ?? 0;
            if (hours > 0) result[colId] = hours;
        }
        return result;
    }

    /// <param name="subRates">Per-worksheet sub rates keyed by LaborTypeId — (SubCostPerHr, SubPricePerHr).</param>
    public static decimal ComputeScenarioSell(
        Dictionary<int, decimal> laborHours,
        IReadOnlyDictionary<int, TblDesignLaborType> laborTypes,
        IReadOnlyDictionary<int, (decimal SubCostPerHr, decimal SubPricePerHr)> subRates,
        ScenarioMode scenario)
    {
        decimal total = 0;
        foreach (var (ltId, hours) in laborHours)
            if (laborTypes.TryGetValue(ltId, out var lt))
            {
                var sub = subRates.TryGetValue(ltId, out var r) ? r.SubPricePerHr : 0;
                total += hours * ScenarioRate(lt, sub, scenario);
            }
        return total;
    }

    /// <param name="subRates">Per-worksheet sub rates keyed by LaborTypeId — (SubCostPerHr, SubPricePerHr).</param>
    public static decimal ComputeScenarioCost(
        Dictionary<int, decimal> laborHours,
        IReadOnlyDictionary<int, TblDesignLaborType> laborTypes,
        IReadOnlyDictionary<int, (decimal SubCostPerHr, decimal SubPricePerHr)> subRates,
        ScenarioMode scenario)
    {
        decimal total = 0;
        foreach (var (ltId, hours) in laborHours)
            if (laborTypes.TryGetValue(ltId, out var lt))
            {
                var sub = subRates.TryGetValue(ltId, out var r) ? r.SubCostPerHr : 0;
                total += hours * ScenarioCostRate(lt, sub, scenario);
            }
        return total;
    }

    /// <param name="travelLaborData">
    /// Per-labor-type traveling headcount. Tuple: (IsTravelingRole, SubRole, PersonCount).
    /// Used to compute scenario-aware multipliers:
    ///   All Us    — all IsTravelingRole types contribute their PersonCount.
    ///   Our Leads — IsTravelingRole types with SubRole != Sub (internal + lead roles stay on-site).
    ///   Subs      — only IsTravelingRole + Internal types (purely internal roles always travel).
    /// </param>
    public static WorksheetSummary ComputeSummary(
        IReadOnlyList<(int Qty, decimal DiscCost, decimal SellPrice)> lineItems,
        Dictionary<int, decimal> laborHours,
        IReadOnlyDictionary<int, TblDesignLaborType> laborTypes,
        IReadOnlyDictionary<int, (decimal SubCostPerHr, decimal SubPricePerHr)> subRates,
        IReadOnlyList<TblDesignWorksheetMiscRow> miscRows,
        IReadOnlyList<TblDesignWorksheetTravelRow> travelRows,
        IReadOnlyList<(bool IsTravelingRole, SubRole SubRole, int PersonCount)> travelLaborData,
        IReadOnlyList<(int Qty, decimal Cost, decimal SellPrice)> materialsItems)
    {
        var hwCost  = lineItems.Sum(i => i.DiscCost * i.Qty);
        var hwSell  = lineItems.Sum(i => i.SellPrice * i.Qty);
        var lcAllUs = ComputeScenarioCost(laborHours, laborTypes, subRates, ScenarioMode.AllUs);
        var lsAllUs = ComputeScenarioSell(laborHours, laborTypes, subRates, ScenarioMode.AllUs);
        var lcLeads = ComputeScenarioCost(laborHours, laborTypes, subRates, ScenarioMode.OurLeads);
        var lsLeads = ComputeScenarioSell(laborHours, laborTypes, subRates, ScenarioMode.OurLeads);
        var lcSubs  = ComputeScenarioCost(laborHours, laborTypes, subRates, ScenarioMode.Subs);
        var lsSubs  = ComputeScenarioSell(laborHours, laborTypes, subRates, ScenarioMode.Subs);
        var miscCost = miscRows.Sum(r => r.Cost * r.Qty);
        var miscSell = miscRows.Sum(r => r.SellPrice * r.Qty);
        var matCost  = materialsItems.Sum(m => m.Cost * m.Qty);
        var matSell  = materialsItems.Sum(m => m.SellPrice * m.Qty);

        // Travel scenario multipliers = number of on-site people per scenario
        int multAllUs   = travelLaborData.Where(l => l.IsTravelingRole).Sum(l => l.PersonCount);
        int multOurLeads = travelLaborData.Where(l => l.IsTravelingRole && l.SubRole != SubRole.Sub).Sum(l => l.PersonCount);
        int multSubs    = travelLaborData.Where(l => l.IsTravelingRole && l.SubRole == SubRole.Internal).Sum(l => l.PersonCount);

        var (tcAllUs, tsAllUs)     = ComputeScenarioTravel(travelRows, multAllUs);
        var (tcLeads, tsLeads)     = ComputeScenarioTravel(travelRows, multOurLeads);
        var (tcSubs, tsSubs)       = ComputeScenarioTravel(travelRows, multSubs);

        return new WorksheetSummary(
            hwCost,  hwSell,
            lcAllUs, lsAllUs,
            lcLeads, lsLeads,
            lcSubs,  lsSubs,
            miscCost, miscSell,
            tcAllUs, tsAllUs,
            tcLeads, tsLeads,
            tcSubs,  tsSubs,
            matCost, matSell);
    }

    /// <summary>
    /// Computes travel cost and sell for a single scenario given the multiplier (number of on-site
    /// people for that scenario).
    /// Per-person items scale with multiplier; flat items are charged once if multiplier > 0.
    /// </summary>
    private static (decimal Cost, decimal Sell) ComputeScenarioTravel(
        IReadOnlyList<TblDesignWorksheetTravelRow> rows, int multiplier)
    {
        decimal cost = 0, sell = 0;
        foreach (var r in rows)
        {
            if (r.IsPerPerson)
            {
                cost += r.Cost * multiplier;
                sell += r.SellPrice * multiplier;
            }
            else if (multiplier > 0)
            {
                // Flat item: charged once as long as anyone travels
                cost += r.Cost;
                sell += r.SellPrice;
            }
            // multiplier == 0 → no one travels → flat item is also $0
        }
        return (cost, sell);
    }

    /// <summary>
    /// Sell rate for a labor type under a given scenario.
    /// Internal: always internal price regardless of scenario.
    /// LeadSub: internal in All Us + Our Leads; sub in All Subs.
    /// Sub: internal only in All Us; sub in Our Leads + All Subs.
    /// </summary>
    public static decimal ScenarioRate(TblDesignLaborType lt, decimal subPricePerHr, ScenarioMode scenario) =>
        scenario switch
        {
            ScenarioMode.AllUs    => lt.InternalPricePerHr,
            ScenarioMode.OurLeads => lt.SubRole == SubRole.Sub
                                         ? subPricePerHr : lt.InternalPricePerHr,
            ScenarioMode.Subs     => lt.SubRole == SubRole.Internal
                                         ? lt.InternalPricePerHr : subPricePerHr,
            _                     => lt.InternalPricePerHr,
        };

    /// <summary>
    /// Cost rate for a labor type under a given scenario (mirrors ScenarioRate but uses cost fields).
    /// </summary>
    public static decimal ScenarioCostRate(TblDesignLaborType lt, decimal subCostPerHr, ScenarioMode scenario) =>
        scenario switch
        {
            ScenarioMode.AllUs    => lt.InternalCostPerHr,
            ScenarioMode.OurLeads => lt.SubRole == SubRole.Sub
                                         ? subCostPerHr : lt.InternalCostPerHr,
            ScenarioMode.Subs     => lt.SubRole == SubRole.Internal
                                         ? lt.InternalCostPerHr : subCostPerHr,
            _                     => lt.InternalCostPerHr,
        };
}
