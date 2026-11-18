using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Shared.Logging;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

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
    private static readonly Regex HexColorRegex = new("^[0-9A-F]{6}$", RegexOptions.Compiled);

    private readonly IDirectorDBContext _db;
    private readonly IAirlineAccessScope _accessScope;
    private readonly IInternalCallerTokenService _tokenService;

    public CuppsAirlinesController(IDirectorDBContext db, IAirlineAccessScope accessScope, IInternalCallerTokenService tokenService)
    {
        _db = db;
        _accessScope = accessScope;
        _tokenService = tokenService;
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

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CuppsAirlineCreateRequest body, CancellationToken ct = default)
    {
        var username = _tokenService.ValidateToken(Request.Headers["X-Internal-Caller-Token"], out var tokenRole);
        AirlineFileLog.Write($"[CREATE-API] received user='{username}' tokenRole='{tokenRole}' payload={System.Text.Json.JsonSerializer.Serialize(body)}");
        if (username is null)
        {
            AirlineFileLog.Write("[CREATE-API] 401: missing or invalid caller token");
            return Unauthorized(new { message = "Missing or invalid caller token." });
        }

        var caller = await _db.TblUsers.AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserPrincipalName == username, ct);

        // A signed-in Director user is judged by the role on file. An Editor bridge user has no row, so the role the
        // signed token carries (set from that session) decides instead.
        // The role on file counts, and so does the signed token's: it carries the session's effective role, which for
        // a custom role is the built-in role it is based on.
        static bool CanCreate(string? role) =>
            string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "Architect", StringComparison.OrdinalIgnoreCase);
        if (!CanCreate(caller?.Role?.RoleName) && !CanCreate(tokenRole))
        {
            // A plain Forbid() would redirect this caller (no cookie) to an HTML page; answer in JSON instead.
            AirlineFileLog.Write($"[CREATE-API] 403: role on file='{caller?.Role?.RoleName}', token role='{tokenRole}' is not Admin or Architect");
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = $"Only an Admin or Architect can create an airline (signed in as {tokenRole ?? caller?.Role?.RoleName ?? "an unknown role"})." });
        }

        var code = (body.IataCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length != 2)
        {
            AirlineFileLog.Write($"[CREATE-API] 400: IataCode '{code}' is not 2 characters");
            return BadRequest(new { message = "IataCode must be exactly 2 characters." });
        }

        var nameEn = (body.NameEnglish ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nameEn))
        {
            AirlineFileLog.Write("[CREATE-API] 400: NameEnglish is empty");
            return BadRequest(new { message = "NameEnglish is required." });
        }

        var nameFr = string.IsNullOrWhiteSpace(body.NameFrench) ? nameEn : body.NameFrench.Trim();
        var cuppsName = string.IsNullOrWhiteSpace(body.CuppsName) ? nameEn : body.CuppsName.Trim();
        var icao = string.IsNullOrWhiteSpace(body.IcaoCode) ? null : body.IcaoCode.Trim().ToUpperInvariant();

        var exists = await _db.TblAirlineAccounts.AsNoTracking()
            .AnyAsync(a => a.IataCode != null && a.IataCode.ToUpper() == code, ct);

        if (exists)
        {
            AirlineFileLog.Write($"[CREATE-API] 409: IATA code {code} already exists");
            return Conflict(new { message = $"An airline with IATA code {code} already exists." });
        }

        var backgroundColor = NormalizeBackgroundColour(body.StandardBackgroundColour);

        // UserGroupId is a required database column because airline supervisor scope is resolved
        // through tblUserGroupMembership. It is not an optional field in the Add Airline wizard,
        // so create (or reuse) an airline-named Director group automatically.
        var userGroup = await _db.TblUserGroups
            .FirstOrDefaultAsync(g => g.GroupName == nameEn || g.GroupName == cuppsName, ct);

        if (userGroup == null)
        {
            userGroup = new DXSuite.Shared.Entities.Identity.TblUserGroup
            {
                GroupName = nameEn,
                Description = $"Airline supervisor scope for {nameEn}",
                IsActive = true,
                GroupType = "director",
                CreateDate = DateTime.UtcNow,
            };
            _db.TblUserGroups.Add(userGroup);
            await _db.SaveChangesAsync(ct);
        }

        var entity = new TblAirlineAccount
        {
            NameEnglish = nameEn,
            NameFrench = nameFr,
            IataCode = code,
            IcaoCode = icao,
            CuppsName = cuppsName,
            IsCanada = body.IsCanada,
            IsUnitedStates = body.IsUnitedStates,
            IsInternational = body.IsInternational,
            StandardBackgroundColour = backgroundColor,
            StandardTextColour = 0,
            DcsProviderId = null,
            AllianceId = null,
            ShowAirportStandardTemplates = true,
            DefaultCounterTemplateId = null,
            PickerTab = 0,
            UserGroupId = userGroup.UserGroupId,
            RetiredUtc = body.Active ? null : DateTime.UtcNow,
            FullTemplateLibraryId = null,
            HalfTemplateLibraryId = null,
        };

        _db.TblAirlineAccounts.Add(entity);
        AirlineFileLog.Write($"[CREATE-API] inserting tblAirlineAccount row: {System.Text.Json.JsonSerializer.Serialize(new { entity.NameEnglish, entity.NameFrench, entity.IataCode, entity.IcaoCode, entity.CuppsName, entity.IsCanada, entity.IsUnitedStates, entity.IsInternational, entity.StandardBackgroundColour, entity.UserGroupId, entity.FullTemplateLibraryId, entity.HalfTemplateLibraryId, entity.RetiredUtc })}");
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // The real reason (a foreign key, column size or missing column) is in the inner exception: log it all.
            var why = AirlineFileLog.Describe(ex);
            AirlineFileLog.Write($"[CREATE-API] 500: saving the airline failed. {why}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = $"The airline could not be saved: {ex.GetBaseException().Message}" });
        }

        AirlineFileLog.Write($"[CREATE-API] 200: created AirlineId={entity.AirlineId} code={entity.IataCode}");
        return Ok(new CuppsAirlineCreatedResponse(
            entity.AirlineId,
            entity.IataCode,
            entity.CuppsName,
            entity.StandardBackgroundColour));
    }

    private static string NormalizeBackgroundColour(string? input)
    {
        var raw = (input ?? string.Empty).Trim();
        if (raw.StartsWith('#')) raw = raw[1..];
        raw = raw.ToUpperInvariant();
        return HexColorRegex.IsMatch(raw) ? $"#{raw}" : "#0071BC";
    }
}

public sealed record CuppsAirlineDto(int AirlineId, string Code, string Name, string? Bg);
public sealed record CuppsAirlineCreateRequest(
    string NameEnglish,
    string NameFrench,
    string IataCode,
    string? IcaoCode,
    string CuppsName,
    bool IsCanada,
    bool IsUnitedStates,
    bool IsInternational,
    string StandardBackgroundColour,
    bool Active = true);

public sealed record CuppsAirlineCreatedResponse(
    int AirlineId,
    string Code,
    string Name,
    string BackgroundColor);
