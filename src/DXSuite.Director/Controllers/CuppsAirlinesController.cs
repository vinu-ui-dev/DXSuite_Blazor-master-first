using DXSuite.Director.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Controllers;

[Route("api/cupps/airlines")]
[ApiController]
[AllowAnonymous]
public sealed class CuppsAirlinesController : ControllerBase
{
    private readonly IDirectorDBContext _db;

    public CuppsAirlinesController(IDirectorDBContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns active CUPPS airline list from Director's airline accounts table.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetActive(CancellationToken ct = default)
    {
        var rows = await _db.TblAirlineAccounts
            .AsNoTracking()
            .Where(a => a.RetiredUtc == null)
            .Where(a => a.IataCode != null && a.IataCode != "")
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
