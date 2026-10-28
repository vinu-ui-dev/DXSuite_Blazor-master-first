using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Security;
using System.Security.Cryptography;

namespace DXSuite.Director.Services
{
    /// <summary>
    /// Returned once at key creation time — the plaintext key is never stored.
    /// </summary>
    public record ApiKeyCreationResult(TblApiKey ApiKey, string PlaintextKey);

    public interface IApiKeyService
    {
        Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey);
        Task<List<TblApiKey>> GetAllApiKeysAsync();
        Task<TblApiKey?> GetApiKeyByIdAsync(int apiKeyId);
        Task<ApiKeyCreationResult> CreateApiKeyAsync(string keyName, int? locationId, string? permissions, int? createdByUserId, DateTime? expiresAt = null);
        Task<TblApiKey> UpdateApiKeyAsync(int apiKeyId, string? keyName, int? locationId, string? permissions, bool? isActive, DateTime? expiresAt);
        Task RevokeApiKeyAsync(int apiKeyId);
        Task DeleteApiKeyAsync(int apiKeyId);
        Task RecordApiKeyUsageAsync(int apiKeyId);
    }

    public class ApiKeyValidationResult
    {
        public bool IsValid { get; set; }
        public int? ApiKeyId { get; set; }
        public string? KeyName { get; set; }
        public int? LocationId { get; set; }
        public string? Permissions { get; set; }
        public string? InvalidReason { get; set; }
    }

    public class ApiKeyService : IApiKeyService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ApiKeyService> _logger;

        public ApiKeyService(IServiceScopeFactory scopeFactory, ILogger<ApiKeyService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    InvalidReason = "API key is empty"
                };
            }

            var hash = GatewayApiKeyHelper.ComputeApiKeyHash(apiKey);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var key = await db.TblApiKeys
                .Include(k => k.Location)
                .FirstOrDefaultAsync(k => k.ApiKeyHash != null && k.ApiKeyHash == hash);

            if (key == null)
            {
                _logger.LogWarning("API key validation failed: key not found");
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    InvalidReason = "API key not found"
                };
            }

            if (!key.IsActive)
            {
                _logger.LogWarning("API key validation failed: key {KeyName} is inactive", key.KeyName);
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    InvalidReason = "API key is inactive"
                };
            }

            if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("API key validation failed: key {KeyName} has expired", key.KeyName);
                return new ApiKeyValidationResult
                {
                    IsValid = false,
                    InvalidReason = "API key has expired"
                };
            }

            // Record usage asynchronously (fire and forget for performance)
            _ = Task.Run(() => RecordApiKeyUsageAsync(key.ApiKeyId));

            return new ApiKeyValidationResult
            {
                IsValid = true,
                ApiKeyId = key.ApiKeyId,
                KeyName = key.KeyName,
                LocationId = key.LocationId,
                Permissions = key.Permissions
            };
        }

        public async Task<List<TblApiKey>> GetAllApiKeysAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblApiKeys
                .Include(k => k.Location)
                .Include(k => k.CreatedByUser)
                .OrderByDescending(k => k.CreateDate)
                .ToListAsync();
        }

        public async Task<TblApiKey?> GetApiKeyByIdAsync(int apiKeyId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblApiKeys
                .Include(k => k.Location)
                .Include(k => k.CreatedByUser)
                .FirstOrDefaultAsync(k => k.ApiKeyId == apiKeyId);
        }

        public async Task<ApiKeyCreationResult> CreateApiKeyAsync(string keyName, int? locationId, string? permissions, int? createdByUserId, DateTime? expiresAt = null)
        {
            var plaintextKey = GenerateSecureApiKey();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var apiKey = new TblApiKey
            {
                ApiKey = "",
                ApiKeyHash = GatewayApiKeyHelper.ComputeApiKeyHash(plaintextKey),
                KeyPrefix = plaintextKey[..8],
                KeyName = keyName,
                LocationId = locationId,
                Permissions = permissions,
                IsActive = true,
                CreatedBy = createdByUserId,
                CreateDate = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                CallCount = 0
            };

            db.TblApiKeys.Add(apiKey);
            await db.SaveChangesAsync();

            _logger.LogInformation("Created new API key: {KeyName} (ID: {ApiKeyId})", keyName, apiKey.ApiKeyId);

            return new ApiKeyCreationResult(apiKey, plaintextKey);
        }

        public async Task<TblApiKey> UpdateApiKeyAsync(int apiKeyId, string? keyName, int? locationId, string? permissions, bool? isActive, DateTime? expiresAt)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var apiKey = await db.TblApiKeys.FindAsync(apiKeyId);
            if (apiKey == null)
            {
                throw new ArgumentException($"API key with ID {apiKeyId} not found");
            }

            if (keyName != null) apiKey.KeyName = keyName;
            if (locationId.HasValue) apiKey.LocationId = locationId == 0 ? null : locationId;
            if (permissions != null) apiKey.Permissions = permissions;
            if (isActive.HasValue) apiKey.IsActive = isActive.Value;
            apiKey.ExpiresAt = expiresAt;
            apiKey.LastEditDate = DateTime.UtcNow;

            await db.SaveChangesAsync();

            _logger.LogInformation("Updated API key: {KeyName} (ID: {ApiKeyId})", apiKey.KeyName, apiKeyId);

            return apiKey;
        }

        public async Task RevokeApiKeyAsync(int apiKeyId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var apiKey = await db.TblApiKeys.FindAsync(apiKeyId);
            if (apiKey == null)
            {
                throw new ArgumentException($"API key with ID {apiKeyId} not found");
            }

            apiKey.IsActive = false;
            apiKey.LastEditDate = DateTime.UtcNow;
            await db.SaveChangesAsync();

            _logger.LogInformation("Revoked API key: {KeyName} (ID: {ApiKeyId})", apiKey.KeyName, apiKeyId);
        }

        public async Task DeleteApiKeyAsync(int apiKeyId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var apiKey = await db.TblApiKeys.FindAsync(apiKeyId);
            if (apiKey == null)
            {
                throw new ArgumentException($"API key with ID {apiKeyId} not found");
            }

            db.TblApiKeys.Remove(apiKey);
            await db.SaveChangesAsync();

            _logger.LogInformation("Deleted API key: {KeyName} (ID: {ApiKeyId})", apiKey.KeyName, apiKeyId);
        }

        public async Task RecordApiKeyUsageAsync(int apiKeyId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

                var apiKey = await db.TblApiKeys.FindAsync(apiKeyId);
                if (apiKey != null)
                {
                    apiKey.CallCount++;
                    apiKey.LastUsedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // Don't fail requests due to usage tracking errors
                _logger.LogWarning(ex, "Failed to record API key usage for key ID {ApiKeyId}", apiKeyId);
            }
        }

        private static string GenerateSecureApiKey()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }
    }
}
