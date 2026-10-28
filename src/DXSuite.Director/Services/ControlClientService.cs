using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Security;

namespace DXSuite.Director.Services;

public class ControlClientService : IControlClientService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ControlClientService> _logger;

    public ControlClientService(IServiceScopeFactory scopeFactory, ILogger<ControlClientService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<List<TblControlClient>> GetAllClientsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblControlClients
            .Include(c => c.Location)
            .Include(c => c.ControlClientDisplays)
            .Include(c => c.ControlClientTokens)
            .OrderBy(c => c.ClientName)
            .ToListAsync();
    }

    public async Task<TblControlClient?> GetClientByIdAsync(int controlClientId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblControlClients
            .Include(c => c.Location)
            .Include(c => c.ControlClientDisplays)
                .ThenInclude(d => d.Display)
            .FirstOrDefaultAsync(c => c.ControlClientId == controlClientId);
    }

    public async Task<TblControlClient> CreateClientAsync(string clientName, string authMode, int locationId, string? accessCode, int? createdBy)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var client = new TblControlClient
        {
            ClientName = clientName,
            AuthMode = authMode,
            LocationId = locationId,
            IsActive = true,
            CreatedBy = createdBy,
            CreateDate = DateTime.UtcNow
        };

        if (authMode == "accesscode" && !string.IsNullOrEmpty(accessCode))
        {
            client.AccessCodeHash = GatewayApiKeyHelper.ComputeApiKeyHash(accessCode);
            client.AccessCodePrefix = accessCode.Length >= 4 ? accessCode[..4] : accessCode;
        }

        db.TblControlClients.Add(client);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created control client '{Name}' (ID: {Id}) at location {LocationId}",
            clientName, client.ControlClientId, locationId);

        return client;
    }

    public async Task UpdateClientAsync(int controlClientId, string clientName, int locationId, bool isActive)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var client = await db.TblControlClients.FindAsync(controlClientId);
        if (client == null)
            throw new ArgumentException($"Control client {controlClientId} not found");

        client.ClientName = clientName;
        client.LocationId = locationId;
        client.IsActive = isActive;
        client.LastEditDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("Updated control client '{Name}' (ID: {Id})", clientName, controlClientId);
    }

    public async Task SetAccessCodeAsync(int controlClientId, string accessCode)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var client = await db.TblControlClients.FindAsync(controlClientId);
        if (client == null)
            throw new ArgumentException($"Control client {controlClientId} not found");

        client.AccessCodeHash = GatewayApiKeyHelper.ComputeApiKeyHash(accessCode);
        client.AccessCodePrefix = accessCode.Length >= 4 ? accessCode[..4] : accessCode;
        client.LastEditDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("Reset access code for control client (ID: {Id})", controlClientId);
    }

    public async Task<bool> ValidateAccessCodeAsync(int controlClientId, string accessCode, string? clientIp)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var client = await db.TblControlClients.FindAsync(controlClientId);
        if (client == null || !client.IsActive)
            return false;

        if (client.AuthMode != "accesscode" || string.IsNullOrEmpty(client.AccessCodeHash))
            return false;

        var hash = GatewayApiKeyHelper.ComputeApiKeyHash(accessCode);
        if (hash != client.AccessCodeHash)
            return false;

        client.LastAuthAt = DateTime.UtcNow;
        client.LastAuthIp = clientIp;
        await db.SaveChangesAsync();

        return true;
    }

    public async Task SetDisplayAssignmentsAsync(int controlClientId, List<int> displayIds)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var existing = await db.TblControlClientDisplays
            .Where(d => d.ControlClientId == controlClientId)
            .ToListAsync();

        db.TblControlClientDisplays.RemoveRange(existing);

        foreach (var displayId in displayIds)
        {
            db.TblControlClientDisplays.Add(new TblControlClientDisplay
            {
                ControlClientId = controlClientId,
                DisplayId = displayId
            });
        }

        await db.SaveChangesAsync();

        _logger.LogInformation("Set {Count} display assignments for control client (ID: {Id})",
            displayIds.Count, controlClientId);
    }

    public async Task<List<TblDisplay>> GetAssignedDisplaysAsync(int controlClientId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var assignments = await db.TblControlClientDisplays
            .Where(d => d.ControlClientId == controlClientId)
            .Select(d => d.DisplayId)
            .ToListAsync();

        if (assignments.Count == 0)
            return new List<TblDisplay>();

        return await db.TblDisplays
            .Where(d => assignments.Contains(d.DisplayId))
            .OrderBy(d => d.DisplayName)
            .ToListAsync();
    }

    public async Task<List<TblDisplay>> GetEffectiveDisplaysAsync(int controlClientId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var client = await db.TblControlClients
            .Include(c => c.ControlClientDisplays)
            .FirstOrDefaultAsync(c => c.ControlClientId == controlClientId);

        if (client == null)
            return new List<TblDisplay>();

        // If no specific display assignments, return all displays at the location
        if (client.ControlClientDisplays.Count == 0)
        {
            return await db.TblDisplays
                .Where(d => d.LocationId == client.LocationId)
                .OrderBy(d => d.DisplayName)
                .ToListAsync();
        }

        var displayIds = client.ControlClientDisplays.Select(d => d.DisplayId).ToList();
        return await db.TblDisplays
            .Where(d => displayIds.Contains(d.DisplayId))
            .OrderBy(d => d.DisplayName)
            .ToListAsync();
    }

    public async Task DeleteClientAsync(int controlClientId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var client = await db.TblControlClients.FindAsync(controlClientId);
        if (client == null)
            throw new ArgumentException($"Control client {controlClientId} not found");

        db.TblControlClients.Remove(client);
        await db.SaveChangesAsync();

        _logger.LogInformation("Deleted control client (ID: {Id})", controlClientId);
    }

    public async Task SetDisplayOverrideAsync(int controlClientId, int displayId, int assetId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        await ValidateDisplayAccessAsync(db, controlClientId, displayId);

        var display = await db.TblDisplays.FindAsync(displayId);
        if (display == null)
            throw new ArgumentException($"Display {displayId} not found");

        display.OverrideAssetId = assetId;
        display.DisplayMode = 1; // Override mode
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IPublicationInvalidator>().DisplayChanged(displayId);

        _logger.LogInformation("Control client {ClientId} set override on display {DisplayId} to asset {AssetId}",
            controlClientId, displayId, assetId);
    }

    public async Task ClearDisplayOverrideAsync(int controlClientId, int displayId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        await ValidateDisplayAccessAsync(db, controlClientId, displayId);

        var display = await db.TblDisplays.FindAsync(displayId);
        if (display == null)
            throw new ArgumentException($"Display {displayId} not found");

        display.OverrideAssetId = null;
        display.DisplayMode = 0; // Normal/Schedule mode
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IPublicationInvalidator>().DisplayChanged(displayId);

        _logger.LogInformation("Control client {ClientId} cleared override on display {DisplayId}",
            controlClientId, displayId);
    }

    public async Task<List<TblAsset>> GetAvailableAssetsAsync(int controlClientId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var client = await db.TblControlClients.FindAsync(controlClientId);
        if (client == null)
            return new List<TblAsset>();

        return await db.TblAssets
            .Where(a => a.LocationId == client.LocationId)
            .OrderBy(a => a.AssetName)
            .ToListAsync();
    }

    private static async Task ValidateDisplayAccessAsync(IDirectorDBContext db, int controlClientId, int displayId)
    {
        var client = await db.TblControlClients
            .Include(c => c.ControlClientDisplays)
            .FirstOrDefaultAsync(c => c.ControlClientId == controlClientId);

        if (client == null)
            throw new ArgumentException($"Control client {controlClientId} not found");

        if (!client.IsActive)
            throw new UnauthorizedAccessException("Control client profile is disabled");

        // If no specific display assignments, check that display belongs to the client's location
        if (client.ControlClientDisplays.Count == 0)
        {
            var display = await db.TblDisplays.FindAsync(displayId);
            if (display == null || display.LocationId != client.LocationId)
                throw new UnauthorizedAccessException($"Display {displayId} is not accessible by this control client");
            return;
        }

        if (!client.ControlClientDisplays.Any(d => d.DisplayId == displayId))
            throw new UnauthorizedAccessException($"Display {displayId} is not assigned to this control client");
    }
}
