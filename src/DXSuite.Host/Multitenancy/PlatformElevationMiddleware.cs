using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services.Identity;

namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Runs after TenantResolutionMiddleware and UseAuthentication.
/// For authenticated requests from platform operators, enforces the JIT elevation requirement:
/// the operator must hold a live TblPlatformElevation row for the current tenant, UNLESS
/// they have a direct tenant membership (bootstrapped via normal login for that tenant).
/// Regular tenant business users are passed through immediately.
/// </summary>
public class PlatformElevationMiddleware
{
    private const string OperatorCachePrefix  = "platform:op:";
    private const string MemberCachePrefix    = "platform:member:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly RequestDelegate _next;
    private readonly ILogger<PlatformElevationMiddleware> _logger;

    public PlatformElevationMiddleware(RequestDelegate next, ILogger<PlatformElevationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip unauthenticated requests — auth middleware handles them.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Skip infrastructure paths that must never be blocked (SignalR negotiation, static files,
        // auth endpoints). /platform/* is handled below: operator identity is always resolved but
        // the elevation row requirement is bypassed for platform admin pages.
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/_blazor",       StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/auth",        StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_framework",  StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/css",         StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/js",          StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/images",      StringComparison.OrdinalIgnoreCase)
            // OIDC callback paths: UseAuthentication processes these internally but the request
            // still flows through this middleware after the handler completes. An operator must
            // not be blocked here — doing so breaks the sign-in flow before the redirect fires.
            || path.StartsWith("/signin-oidc", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/signout",     StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Platform admin paths (/platform/*) access platform metadata rather than tenant data.
        // Authentication and operator identity resolution still run (see below), but the
        // elevation row requirement is skipped so operators can reach the portal before
        // they have an active elevation for the current tenant.
        var isPlatformPath = path.StartsWith("/platform/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/platform", StringComparison.OrdinalIgnoreCase);

        // Resolve optional Platform DB services. When the Platform DB is not configured,
        // all operators pass through — elevation enforcement is not active.
        var platformDb = context.RequestServices.GetService<IPlatformDbContext>();
        if (platformDb == null)
        {
            await _next(context);
            return;
        }

        var tenantContext = context.RequestServices.GetService<ITenantContext>();
        if (tenantContext == null)
        {
            await _next(context);
            return;
        }

        var email = GetEmail(context.User);
        if (email == null || !email.Contains('@'))
        {
            await _next(context);
            return;
        }

        // Determine if this user is a platform operator. Cache per email to avoid a
        // Platform DB hit on every request.
        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        var operatorCacheKey = $"{OperatorCachePrefix}{email}";

        if (!cache.TryGetValue(operatorCacheKey, out PlatformOperatorInfo? opInfo))
        {
            var platformUser = await platformDb.TblPlatformUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.PrimaryEmail == email.ToLowerInvariant(),
                    context.RequestAborted);

            opInfo = platformUser is { IsPlatformOperator: true }
                ? new PlatformOperatorInfo(platformUser.PlatformUserId)
                : null;

            cache.Set(operatorCacheKey, opInfo, CacheTtl);
        }

        // Non-operators pass through immediately.
        if (opInfo == null)
        {
            await _next(context);
            return;
        }

        // Operators who have a direct membership to the current tenant (bootstrapped via their
        // own login) are treated as regular users for that tenant — no elevation required.
        var memberCacheKey = $"{MemberCachePrefix}{opInfo.PlatformUserId}:{tenantContext.TenantId}";
        if (!cache.TryGetValue(memberCacheKey, out bool hasMembership))
        {
            hasMembership = await platformDb.TblPlatformUserTenantMemberships
                .AsNoTracking()
                .AnyAsync(m => m.PlatformUserId == opInfo.PlatformUserId
                             && m.TenantId      == tenantContext.TenantId
                             && m.IsActive,
                    context.RequestAborted);

            cache.Set(memberCacheKey, hasMembership, CacheTtl);
        }

        if (hasMembership)
        {
            await _next(context);
            return;
        }

        // Platform admin pages are metadata-only — no tenant data access — so the elevation
        // row requirement is skipped. Authentication and the operator identity check above
        // still ran, so IsPlatformOperator is correctly set in session state.
        if (isPlatformPath)
        {
            await _next(context);
            return;
        }

        // Platform operator accessing a tenant they are not a member of:
        // require a live elevation row.
        var elevationService = context.RequestServices.GetService<IPlatformElevationService>();
        var auditService     = context.RequestServices.GetService<IPlatformAuditService>();

        var liveElevation = elevationService != null
            ? await elevationService.GetLiveElevationAsync(opInfo.PlatformUserId, tenantContext.TenantId)
            : null;

        if (liveElevation == null)
        {
            _logger.LogWarning(
                "Platform operator {Email} denied access to tenant {TenantId} — no live elevation",
                email, tenantContext.TenantId);

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "elevation_required",
                message = "A live elevation is required to access this tenant.",
                requestElevationUrl = "/platform/elevation/request"
            }), context.RequestAborted);
            return;
        }

        // Write-method requests require TenantSupportWrite; TenantSupport (read-only) is not
        // sufficient for POST/PUT/PATCH/DELETE — block before any state mutation occurs.
        var isWrite = context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE";
        if (isWrite && liveElevation.RoleName != PlatformRoles.TenantSupportWrite)
        {
            _logger.LogWarning(
                "Platform operator {Email} blocked write to tenant {TenantId} — elevation role {Role} is read-only",
                email, tenantContext.TenantId, liveElevation.RoleName);

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "write_elevation_required",
                message = "Write access requires a TenantSupportWrite elevation. Your current elevation is read-only."
            }), context.RequestAborted);
            return;
        }

        // Write an ElevationUsed audit event. The audit service uses its own scope internally
        // so this fire-and-forget is independent of the request scope lifetime.
        if (auditService != null)
        {
            _ = auditService.WriteAsync(
                AuditEventType.ElevationUsed,
                opInfo.PlatformUserId,
                tenantContext.TenantId,
                resourcePath: path,
                ticketRef: liveElevation.TicketRef,
                reason: liveElevation.Reason,
                elevationId: liveElevation.ElevationId,
                operatorDisplayName: GetDisplayName(context.User));
        }

        await _next(context);
    }

    private static string? GetEmail(ClaimsPrincipal user)
        => user.FindFirstValue("preferred_username")
           ?? user.FindFirstValue(ClaimTypes.Email)
           ?? user.Identity?.Name;

    private static string? GetDisplayName(ClaimsPrincipal user)
        => user.FindFirstValue("name")
           ?? user.FindFirstValue(ClaimTypes.GivenName)
           ?? user.Identity?.Name;

    private record PlatformOperatorInfo(Guid PlatformUserId);
}

public static class PlatformElevationMiddlewareExtensions
{
    public static IApplicationBuilder UsePlatformElevation(this IApplicationBuilder app)
        => app.UseMiddleware<PlatformElevationMiddleware>();
}
