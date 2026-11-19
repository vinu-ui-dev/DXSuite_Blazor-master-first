using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Airlines;
using DXSuite.Shared.Entities.Identity;
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
    private readonly AirlineCallerResolver _callers;
    private readonly AirlineProfileSchema _schema;

    public CuppsAirlinesController(IDirectorDBContext db, IAirlineAccessScope accessScope, AirlineCallerResolver callers, AirlineProfileSchema schema)
    {
        _db = db;
        _accessScope = accessScope;
        _callers = callers;
        _schema = schema;
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
        AirlineFileLog.Write($"[CREATE-API] received payload={System.Text.Json.JsonSerializer.Serialize(body)}");
        var caller = await _callers.ResolveAsync(Request, ct);
        if (caller is null)
        {
            AirlineFileLog.Write("[CREATE-API] 401: missing or invalid caller token");
            return Unauthorized(new { message = "Missing or invalid caller token." });
        }
        var username = caller.Username;
        AirlineFileLog.Write($"[CREATE-API] caller user='{username}' role='{caller.RoleName}'");

        // A plain Forbid() would redirect this caller (no cookie) to an HTML page; answer in JSON instead.
        if (!caller.CanManage)
        {
            AirlineFileLog.Write($"[CREATE-API] 403: role '{caller.RoleName}' is not Admin or Architect");
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = $"Only an Admin or Architect can create an airline (signed in as {caller.RoleName ?? "an unknown role"})." });
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

        if (body.AllianceId is { } allianceId && !await _db.TblAlliances.AnyAsync(a => a.AllianceId == allianceId, ct))
            return BadRequest(new { message = "That alliance does not exist." });
        if (body.DcsProviderId is { } dcsId && !await _db.TblDcsProviders.AnyAsync(p => p.DcsProviderId == dcsId, ct))
            return BadRequest(new { message = "That DCS provider does not exist." });

        // The options table must be there before anything is inserted.
        try { await _schema.EnsureAsync(ct); }
        catch (Exception ex)
        {
            AirlineFileLog.Write($"[CREATE-API] 500: options table unavailable. {AirlineFileLog.Describe(ex)}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "The airline options table is missing and could not be created. Run db/migrations/director-airline-profile.sql." });
        }

        // The airline owns a user group (named after it, which is how an Airline Supervisor is matched to it). Reuse
        // the group of that name if there is one; otherwise create it. A fixed id would break on a database without it.
        var groupName = nameEn.Length > 100 ? nameEn[..100] : nameEn;
        int userGroupId;
        try
        {
            var group = await _db.TblUserGroups.FirstOrDefaultAsync(g => g.GroupName == groupName, ct);
            if (group is null)
            {
                group = new TblUserGroup
                {
                    GroupName = groupName,
                    GroupType = "director",
                    Description = $"Users of {nameEn}",
                    IsActive = true,
                    CreateDate = DateTime.UtcNow,
                    LastEditUser = username.Length > 50 ? username[..50] : username,
                };
                _db.TblUserGroups.Add(group);
                await _db.SaveChangesAsync(ct);
                AirlineFileLog.Write($"[CREATE-API] created user group '{groupName}' (UserGroupId {group.UserGroupId}) for the airline");
            }
            else
            {
                AirlineFileLog.Write($"[CREATE-API] reusing user group '{groupName}' (UserGroupId {group.UserGroupId})");
            }
            userGroupId = group.UserGroupId;
        }
        catch (Exception ex)
        {
            AirlineFileLog.Write($"[CREATE-API] 500: creating the user group '{groupName}' failed. {AirlineFileLog.Describe(ex)}");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = $"The airline's user group could not be created: {ex.GetBaseException().Message}" });
        }

        // The template libraries are linked only if they exist in this database.
        var fullLibraryId = await _db.TblLocations.AsNoTracking().AnyAsync(l => l.LocationId == 12, ct) ? 12 : (int?)null;
        var halfLibraryId = await _db.TblLocations.AsNoTracking().AnyAsync(l => l.LocationId == 13, ct) ? 13 : (int?)null;

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
            DcsProviderId = body.DcsProviderId,
            AllianceId = body.AllianceId,
            ShowAirportStandardTemplates = true,
            DefaultCounterTemplateId = null,
            PickerTab = 0,
            UserGroupId = userGroupId,
            RetiredUtc = body.Active ? null : DateTime.UtcNow,
            FullTemplateLibraryId = fullLibraryId,
            HalfTemplateLibraryId = halfLibraryId,
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

        // The airline is active at once: only logos go through approval. Its CUPPS options and terminals are saved with it.
        try
        {
            _db.TblAirlineProfiles.Add(new TblAirlineProfile
            {
                AirlineId = entity.AirlineId,
                SunflowerOption = body.Sunflower,
                AslVideos = body.AslVideos,
                DestinationZone = body.DestinationZone,
            });

            var wanted = (body.TerminalZoneIds ?? []).Distinct().ToList();
            if (wanted.Count > 0)
            {
                var real = await _db.TblZones.AsNoTracking().Where(z => wanted.Contains(z.ZoneId)).Select(z => z.ZoneId).ToListAsync(ct);
                foreach (var zoneId in real)
                    _db.TblAirlineTerminals.Add(new TblAirlineTerminal { AirlineId = entity.AirlineId, ZoneId = zoneId });
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // The airline is only half saved without its options and terminals: take it back out instead.
            AirlineFileLog.Write($"[CREATE-API] 500: saving the airline options failed, removing airline {entity.AirlineId}. {AirlineFileLog.Describe(ex)}");
            try
            {
                foreach (var entry in ((DirectorDbContext)_db).ChangeTracker.Entries().Where(e => e.Entity is TblAirlineProfile or TblAirlineTerminal).ToList())
                    entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                _db.TblAirlineAccounts.Remove(entity);
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception undo) { AirlineFileLog.Write($"[CREATE-API] could not remove airline {entity.AirlineId}: {AirlineFileLog.Describe(undo)}"); }
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
    bool Active = true,
    int? AllianceId = null,
    int? DcsProviderId = null,
    int[]? TerminalZoneIds = null,
    bool Sunflower = false,
    bool AslVideos = true,
    bool DestinationZone = true);

public sealed record CuppsAirlineCreatedResponse(
    int AirlineId,
    string Code,
    string Name,
    string BackgroundColor);
