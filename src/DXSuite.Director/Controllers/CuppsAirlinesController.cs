using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Still [AllowAnonymous]: this app's own CUPPS page loads this endpoint via an internal server-to-self
/// HTTP call (CuppsLiveDataProvider) that doesn't carry the browser's auth cookie, so a hard [Authorize]
/// here would break that load path. <see cref="IAirlineAccessScope"/> instead scopes results down for an
/// Airline Supervisor only when a real authenticated session IS present on the request (e.g. a
/// supervisor calling this URL directly from their own browser) — every other caller, authenticated or
/// not, keeps today's unrestricted behavior.
/// </summary>
[Route("api/cupps/airlines")]
[ApiController]
[AllowAnonymous]
public sealed class CuppsAirlinesController : ControllerBase
{
    private readonly IDirectorDBContext _db;
    private readonly IAirlineAccessScope _accessScope;

    public CuppsAirlinesController(IDirectorDBContext db, IAirlineAccessScope accessScope)
    {
        _db = db;
        _accessScope = accessScope;
    }

    /// <summary>
    /// Returns active CUPPS airline list from Director's airline accounts table, scoped to the caller's
    /// own airline if they're a signed-in Airline Supervisor.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetActive(CancellationToken ct = default)
    {
        var allowedAirlineIds = await _accessScope.GetAllowedAirlineIdsAsync(ct);

        var query = _db.TblAirlineAccounts
            .AsNoTracking()
            .Where(a => a.RetiredUtc == null)
            .Where(a => a.IataCode != null && a.IataCode != "");

        if (allowedAirlineIds is not null)
            query = query.Where(a => allowedAirlineIds.Contains(a.AirlineId));

        var rows = await query
            .Select(a => new
            {
                a.AirlineId,
                a.IataCode,
                a.CuppsName,
                a.NameEnglish,
                a.StandardBackgroundColour
            })
            .ToListAsync(ct);

        var result = rows
            .Where(a => !string.IsNullOrWhiteSpace(a.IataCode))
            .GroupBy(a => a.IataCode!.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var a = g.First();
                return new CuppsAirlineDto(
                    a.AirlineId,
                    g.Key,
                    (a.CuppsName ?? a.NameEnglish ?? g.Key).Trim(),
                    string.IsNullOrWhiteSpace(a.StandardBackgroundColour) ? null : a.StandardBackgroundColour.Trim());
            })
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(result);
    }
}

public sealed record CuppsAirlineDto(int AirlineId, string Code, string Name, string? Bg);
