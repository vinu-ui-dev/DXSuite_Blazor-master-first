using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Services;

/// <summary>
/// Hourly background service that sends notifications when API keys are
/// approaching expiration (14-day and 7-day warnings).
/// </summary>
public class ApiKeyExpiryNotificationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<ApiKeyExpiryNotificationService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public ApiKeyExpiryNotificationService(
        IServiceScopeFactory scopeFactory,
        IDistributedLockService lockService,
        ILogger<ApiKeyExpiryNotificationService> logger)
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
                "api-key-expiry-notifications", TimeSpan.FromMinutes(5), stoppingToken);
            if (distLock == null) continue;

            try
            {
                await CheckExpiringApiKeys(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in API key expiry notification cycle");
            }
        }
    }

    private async Task CheckExpiringApiKeys(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<DXSuite.Shared.Services.INotificationService>();

        var now = DateTime.UtcNow;
        var fourteenDayCutoff = now.AddDays(14);

        // Active keys expiring within the next 14 days
        var expiringKeys = await db.TblApiKeys
            .AsNoTracking()
            .Where(k => k.IsActive && k.ExpiresAt != null && k.ExpiresAt.Value > now && k.ExpiresAt.Value <= fourteenDayCutoff)
            .Select(k => new { k.ApiKeyId, k.KeyName, k.ExpiresAt })
            .ToListAsync(ct);

        if (expiringKeys.Count == 0) return;

        var adminUserIds = await db.TblUsers
            .AsNoTracking()
            .Where(u => u.Role.RoleName == "Admin")
            .Select(u => u.UserId)
            .ToListAsync(ct);

        if (adminUserIds.Count == 0) return;

        // Collect all candidate reference IDs for dedup lookup
        var candidateRefIds = new List<int>();
        foreach (var key in expiringKeys)
        {
            candidateRefIds.Add(key.ApiKeyId);       // 14-day tier
            candidateRefIds.Add(-key.ApiKeyId);      // 7-day tier
        }

        var existingNotificationRefs = await db.TblNotifications
            .AsNoTracking()
            .Where(n => n.NotificationType == "api-key-expiring"
                     && n.ReferenceId != null
                     && candidateRefIds.Contains(n.ReferenceId.Value))
            .Select(n => n.ReferenceId!.Value)
            .Distinct()
            .ToListAsync(ct);

        int sent = 0;
        foreach (var key in expiringKeys)
        {
            if (ct.IsCancellationRequested) break;

            var daysUntilExpiry = (int)Math.Ceiling((key.ExpiresAt!.Value - now).TotalDays);

            // 14-day warning: positive ReferenceId
            if (daysUntilExpiry <= 14 && !existingNotificationRefs.Contains(key.ApiKeyId))
            {
                await notificationService.CreateBulkNotificationsAsync(
                    adminUserIds,
                    $"API key '{key.KeyName}' expires in {daysUntilExpiry} days",
                    $"The API key '{key.KeyName}' will expire on {key.ExpiresAt.Value:MMM d, yyyy}. Rotate or extend it to avoid service disruption.",
                    "api-key-expiring",
                    key.ApiKeyId,
                    "/Settings");
                sent++;
            }

            // 7-day warning: negative ReferenceId to distinguish from the 14-day notification
            if (daysUntilExpiry <= 7 && !existingNotificationRefs.Contains(-key.ApiKeyId))
            {
                await notificationService.CreateBulkNotificationsAsync(
                    adminUserIds,
                    $"API key '{key.KeyName}' expires in {daysUntilExpiry} days \u2014 action required",
                    $"The API key '{key.KeyName}' will expire on {key.ExpiresAt.Value:MMM d, yyyy}. Immediate action is needed to prevent service disruption.",
                    "api-key-expiring",
                    -key.ApiKeyId,
                    "/Settings");
                sent++;
            }
        }

        if (sent > 0)
        {
            _logger.LogInformation("Sent {Count} API key expiry notifications", sent);
        }
    }
}
