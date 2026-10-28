using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Shared.Services.Identity;

public class PlatformIdentityService : IPlatformIdentityService
{
    private readonly IPlatformDbContext _db;
    private readonly ITenantStore _tenantStore;
    private readonly IPlatformAuditService _audit;
    private readonly ReAuthConfirmationService _reAuth;
    private readonly ILogger<PlatformIdentityService> _logger;

    public PlatformIdentityService(IPlatformDbContext db, ITenantStore tenantStore,
        IPlatformAuditService audit, ReAuthConfirmationService reAuth,
        ILogger<PlatformIdentityService> logger)
    {
        _db = db;
        _tenantStore = tenantStore;
        _audit = audit;
        _reAuth = reAuth;
        _logger = logger;
    }

    public async Task<PlatformBootstrapResult?> BootstrapAsync(string email, string tenantId, int tenantUserId)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var normalizedEmail = email.Trim().ToLowerInvariant();

        // Find or create the platform user by email (case-insensitive lookup).
        var platformUser = await _db.TblPlatformUsers
            .FirstOrDefaultAsync(u => u.PrimaryEmail == normalizedEmail);

        if (platformUser == null)
        {
            platformUser = new TblPlatformUser
            {
                PrimaryEmail = normalizedEmail,
                DisplayName = email,
                CreatedDate = DateTime.UtcNow
            };
            _db.TblPlatformUsers.Add(platformUser);
            _logger.LogInformation("Platform identity created for {Email}", normalizedEmail);
        }

        // Update last login timestamp.
        platformUser.LastLoginDate = DateTime.UtcNow;

        // Save user first — membership FK requires the user row to exist before insert.
        await _db.SaveChangesAsync();

        // Find or create the tenant membership for this (platformUser, tenant) pair.
        var membership = await _db.TblPlatformUserTenantMemberships
            .FirstOrDefaultAsync(m => m.PlatformUserId == platformUser.PlatformUserId && m.TenantId == tenantId);

        if (membership == null)
        {
            membership = new TblPlatformUserTenantMembership
            {
                PlatformUserId = platformUser.PlatformUserId,
                TenantId = tenantId,
                TenantUserId = tenantUserId,
                IsActive = true,
                GrantedDate = DateTime.UtcNow
            };
            _db.TblPlatformUserTenantMemberships.Add(membership);
            _logger.LogInformation("Tenant membership bootstrapped for {PlatformUserId} → tenant {TenantId}",
                platformUser.PlatformUserId, tenantId);
        }
        else if (membership.TenantUserId != tenantUserId)
        {
            // TenantUserId can drift if the user was deleted and recreated in the tenant DB.
            membership.TenantUserId = tenantUserId;
        }

        await _db.SaveChangesAsync();

        var membershipCount = await _db.TblPlatformUserTenantMemberships
            .CountAsync(m => m.PlatformUserId == platformUser.PlatformUserId && m.IsActive);

        return new PlatformBootstrapResult(platformUser.PlatformUserId, membershipCount, platformUser.IsPlatformOperator);
    }

    public async Task<IReadOnlyList<TenantMembershipInfo>> GetTenantMembershipsAsync(Guid platformUserId)
    {
        var memberships = await _db.TblPlatformUserTenantMemberships
            .AsNoTracking()
            .Where(m => m.PlatformUserId == platformUserId && m.IsActive)
            .ToListAsync();

        var allTenants = await _tenantStore.GetAllEnabledAsync();
        var tenantMap = allTenants.ToDictionary(t => t.TenantId, t => t);

        var result = new List<TenantMembershipInfo>();
        foreach (var m in memberships)
        {
            if (tenantMap.TryGetValue(m.TenantId, out var record))
                result.Add(new TenantMembershipInfo(record.TenantId, record.Subdomain, record.DisplayName));
        }
        return result;
    }

    public async Task<IReadOnlyList<PlatformUserSummary>> GetPlatformUsersAsync(string? tenantId = null)
    {
        var allTenants = await _tenantStore.GetAllEnabledAsync();
        var tenantMap = allTenants.ToDictionary(t => t.TenantId, t => t.DisplayName);

        var usersQuery = _db.TblPlatformUsers.AsNoTracking();

        IQueryable<TblPlatformUser> filtered;
        if (tenantId is not null)
        {
            var tenantUserIds = _db.TblPlatformUserTenantMemberships
                .AsNoTracking()
                .Where(m => m.TenantId == tenantId)
                .Select(m => m.PlatformUserId);
            filtered = usersQuery.Where(u => tenantUserIds.Contains(u.PlatformUserId));
        }
        else
        {
            filtered = usersQuery;
        }

        var users = await filtered.OrderBy(u => u.PrimaryEmail).ToListAsync();
        var allMemberships = await _db.TblPlatformUserTenantMemberships
            .AsNoTracking()
            .Where(m => users.Select(u => u.PlatformUserId).Contains(m.PlatformUserId))
            .ToListAsync();

        var membershipsByUser = allMemberships
            .GroupBy(m => m.PlatformUserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return users.Select(u =>
        {
            var memberships = membershipsByUser.TryGetValue(u.PlatformUserId, out var ms)
                ? ms.Select(m => new MembershipSummary(
                    m.MembershipId,
                    m.TenantId,
                    tenantMap.TryGetValue(m.TenantId, out var dn) ? dn : m.TenantId,
                    m.IsActive,
                    m.IsTenantAdmin,
                    m.GrantedDate)).ToList()
                : new List<MembershipSummary>();

            return new PlatformUserSummary(u.PlatformUserId, u.PrimaryEmail, u.DisplayName,
                u.IsPlatformOperator, memberships);
        }).ToList();
    }

    public async Task SetTenantAdminAsync(Guid membershipId, bool isTenantAdmin, Guid actingPlatformUserId, Guid reAuthToken)
    {
        // Service-layer re-auth enforcement: token must have been issued by ReAuthConfirmationService
        // for this circuit within the last 2 minutes and not yet consumed.
        RequireReAuth(reAuthToken);

        var membership = await _db.TblPlatformUserTenantMemberships.FindAsync(membershipId)
            ?? throw new InvalidOperationException($"Membership {membershipId} not found.");

        membership.IsTenantAdmin = isTenantAdmin;
        await _db.SaveChangesAsync();

        var eventType = isTenantAdmin ? AuditEventType.TenantAdminGranted : AuditEventType.TenantAdminRevoked;
        await _audit.WriteAsync(eventType, actingPlatformUserId, membership.TenantId);

        _logger.LogInformation(
            "IsTenantAdmin={Value} set on membership {MembershipId} (tenant {TenantId}) by {ActingUser}",
            isTenantAdmin, membershipId, membership.TenantId, actingPlatformUserId);
    }

    public async Task SetMembershipActiveAsync(Guid membershipId, bool isActive, Guid actingPlatformUserId, Guid reAuthToken)
    {
        RequireReAuth(reAuthToken);

        var membership = await _db.TblPlatformUserTenantMemberships.FindAsync(membershipId)
            ?? throw new InvalidOperationException($"Membership {membershipId} not found.");

        membership.IsActive = isActive;
        await _db.SaveChangesAsync();

        var eventType = isActive ? AuditEventType.MembershipGranted : AuditEventType.MembershipRevoked;
        await _audit.WriteAsync(eventType, actingPlatformUserId, membership.TenantId);

        _logger.LogInformation(
            "Membership {MembershipId} IsActive={Value} for tenant {TenantId} set by {ActingUser}",
            membershipId, isActive, membership.TenantId, actingPlatformUserId);
    }

    public async Task<bool> IsTenantAdminAsync(Guid platformUserId, string tenantId)
        => await _db.TblPlatformUserTenantMemberships
            .AsNoTracking()
            .AnyAsync(m => m.PlatformUserId == platformUserId
                        && m.TenantId       == tenantId
                        && m.IsActive
                        && m.IsTenantAdmin);

    public async Task<bool> HasPlatformRoleAsync(Guid platformUserId, string roleName)
        => await _db.TblPlatformUserRoles
            .AsNoTracking()
            .Include(r => r.Role)
            .AnyAsync(r => r.PlatformUserId == platformUserId && r.Role.RoleName == roleName);

    /// <summary>
    /// Validates and immediately consumes the re-auth token. Throws if the token is
    /// expired, already consumed, or was not issued by this circuit's ReAuthConfirmationService.
    /// </summary>
    private void RequireReAuth(Guid token)
    {
        if (!_reAuth.ValidateToken(token))
            throw new UnauthorizedAccessException(
                "Re-authentication confirmation has expired or is invalid. Please confirm the action again.");
        _reAuth.ConsumeToken();
    }
}
