using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Services;

/// <summary>
/// Hourly background service that sends notifications when cloud connector or
/// LLM provider credentials are approaching their admin-set expiration dates
/// (14-day and 7-day warnings).
/// </summary>
public class CredentialExpiryNotificationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<CredentialExpiryNotificationService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    // LLM provider IDs are offset to avoid collision with connector IDs
    private const int LlmProviderIdOffset = 100_000;

    public CredentialExpiryNotificationService(
        IServiceScopeFactory scopeFactory,
        IDistributedLockService lockService,
        ILogger<CredentialExpiryNotificationService> logger)
    {
        _scopeFactory = scopeFactory;
        _lockService = lockService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            using var distLock = await _lockService.TryAcquireLockAsync(
                "credential-expiry-notifications", TimeSpan.FromMinutes(5), stoppingToken);
            if (distLock == null) continue;

            try
            {
                await CheckExpiringCredentials(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in credential expiry notification cycle");
            }
        }
    }

    private async Task CheckExpiringCredentials(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<DXSuite.Shared.Services.INotificationService>();

        var now = DateTime.UtcNow;
        var fourteenDayCutoff = now.AddDays(14);

        // Enabled connectors with credential expiration within 14 days
        var expiringConnectors = await db.TblCloudPlatformConnectors
            .AsNoTracking()
            .Where(c => c.IsEnabled
                     && c.CredentialExpiresAt != null
                     && c.CredentialExpiresAt.Value > now
                     && c.CredentialExpiresAt.Value <= fourteenDayCutoff)
            .Select(c => new { c.ConnectorId, c.DisplayName, c.CredentialExpiresAt })
            .ToListAsync(ct);

        // Enabled LLM providers with credential expiration within 14 days
        var expiringProviders = await db.TblLlmProviderConfigurations
            .AsNoTracking()
            .Where(p => p.IsEnabled
                     && p.CredentialExpiresAt != null
                     && p.CredentialExpiresAt.Value > now
                     && p.CredentialExpiresAt.Value <= fourteenDayCutoff)
            .Select(p => new { p.ConfigId, p.DisplayName, p.CredentialExpiresAt })
            .ToListAsync(ct);

        if (expiringConnectors.Count == 0 && expiringProviders.Count == 0) return;

        var adminUserIds = await db.TblUsers
            .AsNoTracking()
            .Where(u => u.Role.RoleName == "Admin")
            .Select(u => u.UserId)
            .ToListAsync(ct);

        if (adminUserIds.Count == 0) return;

        // Build candidate reference IDs for dedup lookup
        var candidateRefIds = new List<int>();
        foreach (var c in expiringConnectors)
        {
            candidateRefIds.Add(c.ConnectorId);       // 14-day tier
            candidateRefIds.Add(-c.ConnectorId);      // 7-day tier
        }
        foreach (var p in expiringProviders)
        {
            candidateRefIds.Add(LlmProviderIdOffset + p.ConfigId);       // 14-day tier
            candidateRefIds.Add(-(LlmProviderIdOffset + p.ConfigId));    // 7-day tier
        }

        var existingRefs = await db.TblNotifications
            .AsNoTracking()
            .Where(n => n.NotificationType == "credential-expiring"
                     && n.ReferenceId != null
                     && candidateRefIds.Contains(n.ReferenceId.Value))
            .Select(n => n.ReferenceId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var existingRefSet = new HashSet<int>(existingRefs);
        int sent = 0;

        // Cloud connectors
        foreach (var c in expiringConnectors)
        {
            if (ct.IsCancellationRequested) break;

            var daysLeft = (int)Math.Ceiling((c.CredentialExpiresAt!.Value - now).TotalDays);

            if (daysLeft <= 14 && !existingRefSet.Contains(c.ConnectorId))
            {
                await notificationService.CreateBulkNotificationsAsync(
                    adminUserIds,
                    $"Cloud connector '{c.DisplayName}' credential expires in {daysLeft} days",
                    $"The credential for cloud connector '{c.DisplayName}' will expire on {c.CredentialExpiresAt.Value:MMM d, yyyy}. Rotate or extend it to avoid service disruption.",
                    "credential-expiring",
                    c.ConnectorId,
                    "/Settings");
                sent++;
            }

            if (daysLeft <= 7 && !existingRefSet.Contains(-c.ConnectorId))
            {
                await notificationService.CreateBulkNotificationsAsync(
                    adminUserIds,
                    $"Cloud connector '{c.DisplayName}' credential expires in {daysLeft} days \u2014 action required",
                    $"The credential for cloud connector '{c.DisplayName}' will expire on {c.CredentialExpiresAt.Value:MMM d, yyyy}. Immediate action is needed to prevent service disruption.",
                    "credential-expiring",
                    -c.ConnectorId,
                    "/Settings");
                sent++;
            }
        }

        // LLM providers
        foreach (var p in expiringProviders)
        {
            if (ct.IsCancellationRequested) break;

            var daysLeft = (int)Math.Ceiling((p.CredentialExpiresAt!.Value - now).TotalDays);
            var refId = LlmProviderIdOffset + p.ConfigId;

            if (daysLeft <= 14 && !existingRefSet.Contains(refId))
            {
                await notificationService.CreateBulkNotificationsAsync(
                    adminUserIds,
                    $"LLM provider '{p.DisplayName}' credential expires in {daysLeft} days",
                    $"The credential for LLM provider '{p.DisplayName}' will expire on {p.CredentialExpiresAt.Value:MMM d, yyyy}. Rotate or extend it to avoid service disruption.",
                    "credential-expiring",
                    refId,
                    "/Settings");
                sent++;
            }

            if (daysLeft <= 7 && !existingRefSet.Contains(-refId))
            {
                await notificationService.CreateBulkNotificationsAsync(
                    adminUserIds,
                    $"LLM provider '{p.DisplayName}' credential expires in {daysLeft} days \u2014 action required",
                    $"The credential for LLM provider '{p.DisplayName}' will expire on {p.CredentialExpiresAt.Value:MMM d, yyyy}. Immediate action is needed to prevent service disruption.",
                    "credential-expiring",
                    -refId,
                    "/Settings");
                sent++;
            }
        }

        if (sent > 0)
        {
            _logger.LogInformation("Sent {Count} credential expiry notifications", sent);
        }
    }
}
