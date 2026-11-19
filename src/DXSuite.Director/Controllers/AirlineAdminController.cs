using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Airlines;
using DXSuite.Shared.Logging;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Everything the Configuration &gt; Airlines tab does to an airline after it exists: the admin list, the pick lists,
/// editing, retiring, approval, and who is in the airline's user group.
/// </summary>
/// <remarks>
/// [AllowAnonymous] for the same loopback reason as <see cref="CuppsAirlinesController"/>: the Blazor circuit calls
/// these over an internal HttpClient without the browser cookie. Every action here asks for the signed
/// X-Internal-Caller-Token instead, and rejects the call without one. Admin and Architect manage airlines; only an
/// Admin approves, declines, retires or reactivates one.
/// </remarks>
[Route("api/cupps/airlines")]
[ApiController]
[AllowAnonymous]
public sealed class AirlineAdminController : ControllerBase
{
    private static readonly Regex HexColorRegex = new("^[0-9A-F]{6}$", RegexOptions.Compiled);

    /// <summary>The roles an airline's people hold. A member can be given one of these here, nothing else.</summary>
    private static readonly string[] MemberRoleNames = ["Airline Supervisor", "Director Airline_Agent"];

    private readonly DirectorDbContext _db;
    private readonly AirlineCallerResolver _callers;
    private readonly AirlineProfileSchema _schema;
    private readonly IAirlineAccessScope _scope;
    private readonly IRolePermissionService _roles;

    public AirlineAdminController(
        DirectorDbContext db,
        AirlineCallerResolver callers,
        AirlineProfileSchema schema,
        IAirlineAccessScope scope,
        IRolePermissionService roles)
    {
        _db = db;
        _callers = callers;
        _schema = schema;
        _scope = scope;
        _roles = roles;
    }

    // ── Reads ────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Alliances, DCS providers, terminal zones and member roles, for the pick lists.</summary>
    [HttpGet("lookups")]
    public async Task<IActionResult> GetLookups(CancellationToken ct)
    {
        var alliances = await _db.TblAlliances.AsNoTracking()
            .Where(a => a.RetiredUtc == null).OrderBy(a => a.Name)
            .Select(a => new AirlineLookupItem(a.AllianceId, a.Name)).ToListAsync(ct);
        var dcs = await _db.TblDcsProviders.AsNoTracking()
            .Where(p => p.RetiredUtc == null).OrderBy(p => p.Name)
            .Select(p => new AirlineLookupItem(p.DcsProviderId, p.Name)).ToListAsync(ct);
        var terminals = await _db.TblZones.AsNoTracking()
            .Where(z => z.RetiredUtc == null && z.ZoneName.StartsWith("Terminal"))
            .OrderBy(z => z.SortOrder).ThenBy(z => z.ZoneName)
            .Select(z => new AirlineLookupItem(z.ZoneId, z.ZoneName)).ToListAsync(ct);
        var memberRoles = (await _roles.GetAllRolesAsync())
            .Where(r => MemberRoleNames.Contains(r.RoleName, StringComparer.OrdinalIgnoreCase))
            .Select(r => new AirlineLookupItem(r.RoleId, r.RoleName)).ToList();
        return Ok(new AirlineLookups(alliances, dcs, terminals, memberRoles));
    }

    /// <summary>
    /// Every airline with its detail. Admin and Architect see all of them, retired and waiting ones too. Anyone else
    /// sees the approved, active airlines their access scope allows.
    /// </summary>
    [HttpGet("admin")]
    public async Task<IActionResult> GetAdminList(CancellationToken ct)
    {
        var caller = await _callers.ResolveAsync(Request, ct);
        if (caller is null) return Unauthorized(new { message = "Missing or invalid caller token." });

        await TryEnsureSchemaAsync(ct);

        var query = _db.TblAirlineAccounts.AsNoTracking().AsQueryable();
        var profiles = await LoadProfilesAsync(ct);

        List<TblAirlineAccountRow> rows;
        if (caller.CanManage)
        {
            rows = await ProjectAsync(query, ct);
        }
        else
        {
            var allowed = await _scope.GetAllowedAirlineIdsForUserAsync(caller.Username, ct);
            if (allowed is not null) query = query.Where(a => allowed.Contains(a.AirlineId));
            rows = (await ProjectAsync(query.Where(a => a.RetiredUtc == null), ct))
                .Where(a => !profiles.TryGetValue(a.AirlineId, out var p) || p.ApprovalStatus == TblAirlineProfile.Approved)
                .ToList();
        }

        var ids = rows.Select(r => r.AirlineId).ToList();
        var terminals = await _db.TblAirlineTerminals.AsNoTracking()
            .Where(t => ids.Contains(t.AirlineId)).ToListAsync(ct);
        var groupIds = rows.Select(r => r.UserGroupId).Distinct().ToList();
        var memberCounts = await _db.TblUserGroupMemberships.AsNoTracking()
            .Where(m => groupIds.Contains(m.UserGroupId))
            .GroupBy(m => m.UserGroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);
        var alliances = await _db.TblAlliances.AsNoTracking().ToDictionaryAsync(a => a.AllianceId, a => a.Name, ct);
        var dcs = await _db.TblDcsProviders.AsNoTracking().ToDictionaryAsync(p => p.DcsProviderId, p => p.Name, ct);

        var result = rows.Select(a =>
        {
            profiles.TryGetValue(a.AirlineId, out var p);
            return new AirlineDetail(
                a.AirlineId, a.IataCode, a.NameEnglish, a.NameFrench, a.IcaoCode, a.CuppsName,
                a.IsCanada, a.IsUnitedStates, a.IsInternational, a.StandardBackgroundColour,
                a.RetiredUtc != null,
                a.AllianceId, a.AllianceId is { } al && alliances.TryGetValue(al, out var an) ? an : null,
                a.DcsProviderId, a.DcsProviderId is { } d && dcs.TryGetValue(d, out var dn) ? dn : null,
                a.UserGroupId, memberCounts.GetValueOrDefault(a.UserGroupId),
                StatusName(p?.ApprovalStatus ?? TblAirlineProfile.Approved),
                p?.RequestedBy, p?.RequestedUtc, p?.DecidedBy, p?.DecidedUtc, p?.DecisionNote,
                p?.SunflowerOption ?? false, p?.AslVideos ?? true, p?.DestinationZone ?? true,
                terminals.Where(t => t.AirlineId == a.AirlineId).Select(t => t.ZoneId).OrderBy(z => z).ToList());
        }).OrderBy(a => a.NameEnglish, StringComparer.OrdinalIgnoreCase).ToList();

        return Ok(result);
    }

    // ── Edit / retire ────────────────────────────────────────────────────────────────────────────────────────────────

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AirlineUpdateRequest body, CancellationToken ct)
    {
        var (caller, denied) = await RequireAsync(manage: true, ct);
        if (denied is not null) return denied;

        var airline = await _db.TblAirlineAccounts.FirstOrDefaultAsync(a => a.AirlineId == id, ct);
        if (airline is null) return NotFound(new { message = "That airline no longer exists." });

        var nameEn = (body.NameEnglish ?? "").Trim();
        if (nameEn.Length == 0) return BadRequest(new { message = "The English name is required." });
        if (nameEn.Length > 100) return BadRequest(new { message = "The English name is at most 100 characters." });

        var nameFr = string.IsNullOrWhiteSpace(body.NameFrench) ? nameEn : body.NameFrench.Trim();
        var cuppsName = string.IsNullOrWhiteSpace(body.CuppsName) ? nameEn : body.CuppsName.Trim();
        if (nameFr.Length > 100 || cuppsName.Length > 100)
            return BadRequest(new { message = "Names are at most 100 characters." });

        if (body.AllianceId is { } allianceId && !await _db.TblAlliances.AnyAsync(a => a.AllianceId == allianceId, ct))
            return BadRequest(new { message = "That alliance does not exist." });
        if (body.DcsProviderId is { } dcsId && !await _db.TblDcsProviders.AnyAsync(p => p.DcsProviderId == dcsId, ct))
            return BadRequest(new { message = "That DCS provider does not exist." });

        try
        {
            var oldName = airline.NameEnglish;

            airline.NameEnglish = nameEn;
            airline.NameFrench = nameFr;
            airline.CuppsName = cuppsName;
            airline.IsCanada = body.IsCanada;
            airline.IsUnitedStates = body.IsUnitedStates;
            airline.IsInternational = body.IsInternational;
            airline.StandardBackgroundColour = NormalizeColour(body.StandardBackgroundColour);
            airline.AllianceId = body.AllianceId;
            airline.DcsProviderId = body.DcsProviderId;

            // The airline's user group carries its name, which is how people are matched to the airline. Keep them
            // in step when the name changes (unless another group already has the new name).
            if (!string.Equals(oldName, nameEn, StringComparison.Ordinal))
            {
                var group = await _db.TblUserGroups.FirstOrDefaultAsync(g => g.UserGroupId == airline.UserGroupId, ct);
                if (group is not null && string.Equals(group.GroupName, oldName, StringComparison.Ordinal)
                    && !await _db.TblUserGroups.AnyAsync(g => g.GroupName == nameEn && g.UserGroupId != group.UserGroupId, ct))
                {
                    group.GroupName = nameEn;
                    group.Description = $"Users of {nameEn}";
                }
            }

            await SyncTerminalsAsync(id, body.TerminalZoneIds, ct);

            await _schema.EnsureAsync(ct);
            var profile = await _db.TblAirlineProfiles.FirstOrDefaultAsync(p => p.AirlineId == id, ct);
            if (profile is null)
            {
                profile = new TblAirlineProfile { AirlineId = id, ApprovalStatus = TblAirlineProfile.Approved, RequestedUtc = DateTime.UtcNow };
                _db.TblAirlineProfiles.Add(profile);
            }
            profile.SunflowerOption = body.SunflowerOption;
            profile.AslVideos = body.AslVideos;
            profile.DestinationZone = body.DestinationZone;

            await _db.SaveChangesAsync(ct);
            AirlineFileLog.Write($"[AIRLINE-API] {caller!.Username} ({caller.RoleName}) updated airline {airline.IataCode} (id {id})");
            return Ok();
        }
        catch (Exception ex)
        {
            AirlineFileLog.Write($"[AIRLINE-API] 500: updating airline {id} failed. {AirlineFileLog.Describe(ex)}");
            return StatusCode(500, new { message = $"The airline could not be saved: {ex.GetBaseException().Message}" });
        }
    }

    [HttpPost("{id:int}/retire")]
    public Task<IActionResult> Retire(int id, CancellationToken ct) => SetRetiredAsync(id, retire: true, ct);

    [HttpPost("{id:int}/reactivate")]
    public Task<IActionResult> Reactivate(int id, CancellationToken ct) => SetRetiredAsync(id, retire: false, ct);

    private async Task<IActionResult> SetRetiredAsync(int id, bool retire, CancellationToken ct)
    {
        var (caller, denied) = await RequireAsync(manage: false, ct);
        if (denied is not null) return denied;

        var airline = await _db.TblAirlineAccounts.FirstOrDefaultAsync(a => a.AirlineId == id, ct);
        if (airline is null) return NotFound(new { message = "That airline no longer exists." });

        airline.RetiredUtc = retire ? airline.RetiredUtc ?? DateTime.UtcNow : null;
        await _db.SaveChangesAsync(ct);
        AirlineFileLog.Write($"[AIRLINE-API] {caller!.Username} {(retire ? "retired" : "reactivated")} airline {airline.IataCode} (id {id})");
        return Ok();
    }

    // ── Approval (Admin only) ────────────────────────────────────────────────────────────────────────────────────────

    [HttpPost("{id:int}/approve")]
    public Task<IActionResult> Approve(int id, [FromBody] AirlineDecisionRequest? body, CancellationToken ct) =>
        DecideAsync(id, TblAirlineProfile.Approved, body?.Note, ct);

    [HttpPost("{id:int}/decline")]
    public Task<IActionResult> Decline(int id, [FromBody] AirlineDecisionRequest? body, CancellationToken ct) =>
        DecideAsync(id, TblAirlineProfile.Declined, body?.Note, ct);

    private async Task<IActionResult> DecideAsync(int id, byte status, string? note, CancellationToken ct)
    {
        var (caller, denied) = await RequireAsync(manage: false, ct);
        if (denied is not null) return denied;

        if (status == TblAirlineProfile.Declined && string.IsNullOrWhiteSpace(note))
            return BadRequest(new { message = "Give a reason for declining the airline." });

        var airline = await _db.TblAirlineAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AirlineId == id, ct);
        if (airline is null) return NotFound(new { message = "That airline no longer exists." });

        await _schema.EnsureAsync(ct);
        var profile = await _db.TblAirlineProfiles.FirstOrDefaultAsync(p => p.AirlineId == id, ct);
        if (profile is null)
        {
            profile = new TblAirlineProfile { AirlineId = id, RequestedUtc = DateTime.UtcNow };
            _db.TblAirlineProfiles.Add(profile);
        }
        profile.ApprovalStatus = status;
        profile.DecidedBy = Trim(caller!.Username, 256);
        profile.DecidedUtc = DateTime.UtcNow;
        profile.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : Trim(note.Trim(), 500);
        await _db.SaveChangesAsync(ct);

        AirlineFileLog.Write($"[AIRLINE-API] {caller.Username} {(status == TblAirlineProfile.Approved ? "approved" : "declined")} airline {airline.IataCode} (id {id}) note='{profile.DecisionNote}'");
        return Ok();
    }

    // ── Members of the airline's user group ──────────────────────────────────────────────────────────────────────────

    [HttpGet("{id:int}/members")]
    public async Task<IActionResult> GetMembers(int id, CancellationToken ct)
    {
        var (_, denied) = await RequireAsync(manage: true, ct);
        if (denied is not null) return denied;

        var groupId = await GroupIdAsync(id, ct);
        if (groupId is null) return NotFound(new { message = "That airline no longer exists." });

        var members = await _db.TblUserGroupMemberships.AsNoTracking()
            .Where(m => m.UserGroupId == groupId)
            .OrderBy(m => m.User.FriendlyName)
            .Select(m => new AirlineMember(m.UserId, m.User.FriendlyName ?? m.User.UserPrincipalName ?? "", m.User.UserPrincipalName ?? "", m.User.Role.RoleName))
            .ToListAsync(ct);
        return Ok(members);
    }

    /// <summary>Users who are not in the group yet and match the search text, for the "add member" picker.</summary>
    [HttpGet("{id:int}/members/candidates")]
    public async Task<IActionResult> SearchCandidates(int id, [FromQuery] string? q, CancellationToken ct)
    {
        var (_, denied) = await RequireAsync(manage: true, ct);
        if (denied is not null) return denied;

        var groupId = await GroupIdAsync(id, ct);
        if (groupId is null) return NotFound(new { message = "That airline no longer exists." });

        q = (q ?? "").Trim();
        if (q.Length < 2) return Ok(Array.Empty<AirlineMember>());

        var inGroup = _db.TblUserGroupMemberships.Where(m => m.UserGroupId == groupId).Select(m => m.UserId);
        var found = await _db.TblUsers.AsNoTracking()
            .Where(u => !inGroup.Contains(u.UserId)
                        && ((u.FriendlyName != null && u.FriendlyName.Contains(q))
                            || (u.UserPrincipalName != null && u.UserPrincipalName.Contains(q))))
            .OrderBy(u => u.FriendlyName)
            .Take(20)
            .Select(u => new AirlineMember(u.UserId, u.FriendlyName ?? u.UserPrincipalName ?? "", u.UserPrincipalName ?? "", u.Role.RoleName))
            .ToListAsync(ct);
        return Ok(found);
    }

    [HttpPost("{id:int}/members")]
    public async Task<IActionResult> AddMember(int id, [FromBody] AirlineMemberAddRequest body, CancellationToken ct)
    {
        var (caller, denied) = await RequireAsync(manage: true, ct);
        if (denied is not null) return denied;

        var groupId = await GroupIdAsync(id, ct);
        if (groupId is null) return NotFound(new { message = "That airline no longer exists." });

        var user = await _db.TblUsers.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == body.UserId, ct);
        if (user is null) return NotFound(new { message = "That user no longer exists." });

        if (await _db.TblUserGroupMemberships.AnyAsync(m => m.UserGroupId == groupId && m.UserId == user.UserId, ct))
            return Conflict(new { message = $"{user.FriendlyName ?? user.UserPrincipalName} is already in this airline's group." });

        if (!string.IsNullOrWhiteSpace(body.RoleName))
        {
            var wanted = MemberRoleNames.FirstOrDefault(n => string.Equals(n, body.RoleName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (wanted is null)
                return BadRequest(new { message = "An airline member can only be an Airline Supervisor or an Airline Agent." });

            var role = (await _roles.GetAllRolesAsync()).FirstOrDefault(r => string.Equals(r.RoleName, wanted, StringComparison.OrdinalIgnoreCase));
            if (role is null)
                return BadRequest(new { message = $"The role '{wanted}' does not exist yet. Create it on Settings > Roles first." });

            // Someone who runs the system must not lose that by being added to an airline.
            var current = user.Role?.RoleName;
            if (string.Equals(current, "Admin", StringComparison.OrdinalIgnoreCase) || string.Equals(current, "Architect", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = $"{user.FriendlyName ?? user.UserPrincipalName} is an {current}. Their role is not changed to an airline role." });

            user.RoleId = role.RoleId;
        }

        _db.TblUserGroupMemberships.Add(new DXSuite.Shared.Entities.Identity.TblUserGroupMembership
        {
            UserGroupId = groupId.Value,
            UserId = user.UserId,
            AddedDate = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        AirlineFileLog.Write($"[AIRLINE-API] {caller!.Username} added user {user.UserPrincipalName} to airline id {id} (group {groupId}) as '{body.RoleName}'");
        return Ok();
    }

    [HttpDelete("{id:int}/members/{userId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int userId, CancellationToken ct)
    {
        var (caller, denied) = await RequireAsync(manage: true, ct);
        if (denied is not null) return denied;

        var groupId = await GroupIdAsync(id, ct);
        if (groupId is null) return NotFound(new { message = "That airline no longer exists." });

        var membership = await _db.TblUserGroupMemberships.FirstOrDefaultAsync(m => m.UserGroupId == groupId && m.UserId == userId, ct);
        if (membership is null) return Ok();

        _db.TblUserGroupMemberships.Remove(membership);
        await _db.SaveChangesAsync(ct);
        AirlineFileLog.Write($"[AIRLINE-API] {caller!.Username} removed user id {userId} from airline id {id} (group {groupId})");
        return Ok();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Admin and Architect pass; with <paramref name="manage"/> false only an Admin does. Answers in JSON, never a redirect.</summary>
    private async Task<(AirlineCaller? Caller, IActionResult? Denied)> RequireAsync(bool manage, CancellationToken ct)
    {
        var caller = await _callers.ResolveAsync(Request, ct);
        if (caller is null)
            return (null, Unauthorized(new { message = "Missing or invalid caller token." }));

        var allowed = manage ? caller.CanManage : caller.IsAdmin;
        if (!allowed)
        {
            AirlineFileLog.Write($"[AIRLINE-API] 403: {caller.Username} ({caller.RoleName}) may not {(manage ? "manage" : "approve or retire")} airlines");
            return (caller, StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = manage
                    ? $"Only an Admin or Architect can do this (signed in as {caller.RoleName ?? "an unknown role"})."
                    : $"Only an Admin can do this (signed in as {caller.RoleName ?? "an unknown role"}).",
            }));
        }
        return (caller, null);
    }

    private async Task<int?> GroupIdAsync(int airlineId, CancellationToken ct) =>
        await _db.TblAirlineAccounts.AsNoTracking()
            .Where(a => a.AirlineId == airlineId)
            .Select(a => (int?)a.UserGroupId)
            .FirstOrDefaultAsync(ct);

    private async Task TryEnsureSchemaAsync(CancellationToken ct)
    {
        try { await _schema.EnsureAsync(ct); }
        catch (Exception ex) { AirlineFileLog.Write($"[AIRLINE-API] profile table unavailable: {AirlineFileLog.Describe(ex)}"); }
    }

    private async Task<Dictionary<int, TblAirlineProfile>> LoadProfilesAsync(CancellationToken ct)
    {
        try { return await _db.TblAirlineProfiles.AsNoTracking().ToDictionaryAsync(p => p.AirlineId, ct); }
        catch (Exception ex)
        {
            AirlineFileLog.Write($"[AIRLINE-API] could not read the airline profiles: {AirlineFileLog.Describe(ex)}");
            return new Dictionary<int, TblAirlineProfile>();
        }
    }

    private static Task<List<TblAirlineAccountRow>> ProjectAsync(IQueryable<TblAirlineAccount> query, CancellationToken ct) =>
        query.Select(a => new TblAirlineAccountRow(
            a.AirlineId, a.IataCode, a.NameEnglish, a.NameFrench, a.IcaoCode, a.CuppsName,
            a.IsCanada, a.IsUnitedStates, a.IsInternational, a.StandardBackgroundColour,
            a.RetiredUtc, a.AllianceId, a.DcsProviderId, a.UserGroupId)).ToListAsync(ct);

    private async Task SyncTerminalsAsync(int airlineId, int[]? wanted, CancellationToken ct)
    {
        var want = (wanted ?? []).Distinct().ToList();
        if (want.Count > 0)
        {
            var real = await _db.TblZones.AsNoTracking().Where(z => want.Contains(z.ZoneId)).Select(z => z.ZoneId).ToListAsync(ct);
            want = want.Where(real.Contains).ToList();
        }

        var have = await _db.TblAirlineTerminals.Where(t => t.AirlineId == airlineId).ToListAsync(ct);
        foreach (var gone in have.Where(t => !want.Contains(t.ZoneId))) _db.TblAirlineTerminals.Remove(gone);
        foreach (var zoneId in want.Where(z => have.All(t => t.ZoneId != z)))
            _db.TblAirlineTerminals.Add(new TblAirlineTerminal { AirlineId = airlineId, ZoneId = zoneId });
    }

    private static string NormalizeColour(string? input)
    {
        var raw = (input ?? "").Trim();
        if (raw.StartsWith('#')) raw = raw[1..];
        raw = raw.ToUpperInvariant();
        return HexColorRegex.IsMatch(raw) ? $"#{raw}" : "#0071BC";
    }

    internal static string StatusName(byte status) => status switch
    {
        TblAirlineProfile.Pending => "Pending",
        TblAirlineProfile.Declined => "Declined",
        _ => "Approved",
    };

    private static string Trim(string value, int max) => value.Length > max ? value[..max] : value;

    private sealed record TblAirlineAccountRow(
        int AirlineId, string IataCode, string NameEnglish, string NameFrench, string? IcaoCode, string CuppsName,
        bool IsCanada, bool IsUnitedStates, bool IsInternational, string StandardBackgroundColour,
        DateTime? RetiredUtc, int? AllianceId, int? DcsProviderId, int UserGroupId);
}

public sealed record AirlineLookupItem(int Id, string Name);

public sealed record AirlineLookups(
    IReadOnlyList<AirlineLookupItem> Alliances,
    IReadOnlyList<AirlineLookupItem> DcsProviders,
    IReadOnlyList<AirlineLookupItem> Terminals,
    IReadOnlyList<AirlineLookupItem> MemberRoles);

public sealed record AirlineDetail(
    int AirlineId, string Code, string NameEnglish, string NameFrench, string? Icao, string CuppsName,
    bool IsCanada, bool IsUnitedStates, bool IsInternational, string BackgroundColour,
    bool Retired,
    int? AllianceId, string? AllianceName,
    int? DcsProviderId, string? DcsProviderName,
    int UserGroupId, int MemberCount,
    string ApprovalStatus, string? RequestedBy, DateTime? RequestedUtc, string? DecidedBy, DateTime? DecidedUtc, string? DecisionNote,
    bool SunflowerOption, bool AslVideos, bool DestinationZone,
    IReadOnlyList<int> TerminalZoneIds);

public sealed record AirlineUpdateRequest(
    string NameEnglish,
    string NameFrench,
    string CuppsName,
    bool IsCanada,
    bool IsUnitedStates,
    bool IsInternational,
    string StandardBackgroundColour,
    int? AllianceId,
    int? DcsProviderId,
    int[]? TerminalZoneIds,
    bool SunflowerOption,
    bool AslVideos,
    bool DestinationZone);

public sealed record AirlineDecisionRequest(string? Note);

public sealed record AirlineMember(int UserId, string Name, string Account, string Role);

public sealed record AirlineMemberAddRequest(int UserId, string? RoleName);
