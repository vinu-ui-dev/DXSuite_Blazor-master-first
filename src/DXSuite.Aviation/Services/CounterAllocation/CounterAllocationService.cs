using DXSuite.Aviation.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Services.CounterAllocation;

/// <summary>A manual allocation as the editor submits it (GTAA FSD 1.2 ALC-xx, simplified: new work, no CoreAPI feed behind it).</summary>
public sealed record CounterAllocationDraft(
    string Counter, string AirlineCode, string AllocationType, string? Destination, string? FlightNumber,
    DateTime StartUtc, DateTime EndUtc);

public enum CounterAllocationSaveOutcome
{
    Saved,
    Overlap,
    InvalidRange,
}

public sealed record CounterAllocationSaveResult(CounterAllocationSaveOutcome Outcome, int AllocationId = 0, string? ConflictingAirline = null);

/// <summary>
/// Manual counter allocation CRUD (GTAA FSD 1.2 section 3.4). This build has no CoreAPI/RMS allocation feed, so
/// every allocation here is hand-entered under <see cref="CounterAllocationOutageState"/>'s manual mode — there
/// is no automatic return, and no frozen "RMS plan" to review against on return. New work, not ported from a
/// reference build.
/// </summary>
public sealed class CounterAllocationService(IDbContextFactory<AviationDbContext> dbFactory)
{
    public async Task<IReadOnlyList<TblCounterAllocation>> GetInRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TblCounterAllocations
            .AsNoTracking()
            .Where(a => a.StartUtc < toUtc && a.EndUtc > fromUtc)
            .OrderBy(a => a.Counter).ThenBy(a => a.StartUtc)
            .ToListAsync(ct);
    }

    public async Task<CounterAllocationSaveResult> CreateAsync(CounterAllocationDraft draft, string? createdBy, CancellationToken ct = default)
    {
        if (draft.EndUtc <= draft.StartUtc) return new CounterAllocationSaveResult(CounterAllocationSaveOutcome.InvalidRange);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Overlap on the same counter is refused (CTR-xx: at most one allocation per counter at a time).
        var overlapping = await db.TblCounterAllocations
            .Where(a => a.Counter == draft.Counter && a.StartUtc < draft.EndUtc && a.EndUtc > draft.StartUtc)
            .Select(a => a.AirlineCode)
            .FirstOrDefaultAsync(ct);
        if (overlapping is not null)
            return new CounterAllocationSaveResult(CounterAllocationSaveOutcome.Overlap, ConflictingAirline: overlapping);

        var row = new TblCounterAllocation
        {
            Counter = draft.Counter,
            AirlineCode = draft.AirlineCode,
            AllocationType = draft.AllocationType,
            Destination = draft.AllocationType == "Dedicated" ? draft.Destination : null,
            FlightNumber = draft.AllocationType == "Dedicated" ? draft.FlightNumber : null,
            StartUtc = draft.StartUtc,
            EndUtc = draft.EndUtc,
            IsManual = true,
            CreatedUtc = DateTime.UtcNow,
            CreatedBy = createdBy,
        };
        db.TblCounterAllocations.Add(row);
        await db.SaveChangesAsync(ct);
        return new CounterAllocationSaveResult(CounterAllocationSaveOutcome.Saved, row.CounterAllocationId);
    }

    public async Task DeleteAsync(int allocationId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.TblCounterAllocations.Where(a => a.CounterAllocationId == allocationId).ExecuteDeleteAsync(ct);
    }
}
