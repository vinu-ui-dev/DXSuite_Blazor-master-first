using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Contracts;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Mqtt;

namespace DXSuite.Sentinel.Services.Gateway;

public interface IGatewayEnrollmentService
{
    Task<GatewayEnrollResponse> EnrollAsync(GatewayEnrollRequest request, CancellationToken ct = default);
}

/// <summary>
/// Validates an enrollment token and self-registers a calling agent as a SiteGateway, issuing it a
/// per-agent API key. Idempotent by machine fingerprint so a re-enroll (lost local identity) returns
/// the same gateway with a freshly rotated key, rather than creating duplicates. Scoped: runs in the
/// caller's tenant request, so the token is looked up in that tenant's Sentinel DB.
/// </summary>
public class GatewayEnrollmentService : IGatewayEnrollmentService
{
    private readonly ISentinelDBContext _db;
    private readonly SentinelMqttOptions _mqtt;
    private readonly ILogger<GatewayEnrollmentService> _logger;

    public GatewayEnrollmentService(ISentinelDBContext db, SentinelMqttOptions mqtt, ILogger<GatewayEnrollmentService> logger)
    {
        _db = db;
        _mqtt = mqtt;
        _logger = logger;
    }

    public async Task<GatewayEnrollResponse> EnrollAsync(GatewayEnrollRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.MachineFingerprint))
            return Fail("Token and machine fingerprint are required");

        var tokenHash = GatewayApiKeyHelper.ComputeApiKeyHash(request.Token.Trim());
        var token = await _db.SentinelEnrollmentTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token is null) return Fail("Invalid enrollment token");
        if (token.IsRevoked) return Fail("Enrollment token has been revoked");
        if (token.ExpiresUtc is { } exp && exp < DateTime.UtcNow) return Fail("Enrollment token has expired");

        var roles = !string.IsNullOrWhiteSpace(request.Roles) ? request.Roles!.Trim() : token.DefaultRoles;
        var apiKey = GatewayApiKeyHelper.GenerateApiKey();
        var apiKeyHash = GatewayApiKeyHelper.ComputeApiKeyHash(apiKey);

        // Idempotent by fingerprint within the token's customer.
        var existing = await _db.SiteGateways.FirstOrDefaultAsync(
            g => g.EnrollmentFingerprint == request.MachineFingerprint && g.CustomerId == token.CustomerId, ct);

        SiteGateway gateway;
        if (existing is not null)
        {
            existing.ApiKeyHash = apiKeyHash; // rotate the key for the returning agent
            existing.SiteName = string.IsNullOrWhiteSpace(request.HostName) ? existing.SiteName : request.HostName;
            existing.IpAddress = request.IpAddress;
            existing.ConfigJson = BuildConfigJson(roles);
            existing.LastEditDate = DateTime.UtcNow;
            gateway = existing;
            _logger.LogInformation("Agent re-enrolled into gateway {GatewayId} ({Host})", gateway.GatewayId, request.HostName);
        }
        else
        {
            // Enforce max-uses only for genuinely new enrollments.
            if (token.MaxUses is { } max && token.UseCount >= max)
                return Fail("Enrollment token has reached its maximum number of uses");

            gateway = new SiteGateway
            {
                CustomerId = token.CustomerId,
                SiteName = string.IsNullOrWhiteSpace(request.HostName) ? "Enrolled agent" : request.HostName,
                ApiKeyHash = apiKeyHash,
                Status = "enrolled",
                IpAddress = request.IpAddress,
                EnrollmentFingerprint = request.MachineFingerprint,
                ConfigJson = BuildConfigJson(roles),
                IsEnabled = true,
                CreateDate = DateTime.UtcNow
            };
            _db.SiteGateways.Add(gateway);
            token.UseCount++;
            _logger.LogInformation("New agent enrolled ({Host}) via token '{Label}'", request.HostName, token.Label);
        }

        await _db.SaveChangesAsync(ct);

        return new GatewayEnrollResponse
        {
            Success = true,
            GatewayId = gateway.GatewayId,
            TenantId = token.CustomerId,
            ApiKey = apiKey,
            Roles = roles,
            BrokerHost = _mqtt.Host,
            BrokerPort = _mqtt.Port
        };
    }

    private static string BuildConfigJson(string? roles) =>
        $"{{\"roles\":\"{roles ?? string.Empty}\"}}";

    private static GatewayEnrollResponse Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
