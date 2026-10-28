using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public class MetadataDefinitionService : IMetadataDefinitionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetadataDefinitionService> _logger;

    public MetadataDefinitionService(IServiceScopeFactory scopeFactory, ILogger<MetadataDefinitionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<List<MetadataDefinitionDto>> GetAllDefinitionsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var entities = await db.TblMetadataDefinitions
            .AsNoTracking()
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.DisplayName)
            .ToListAsync();

        return entities.Select(ToDto).ToList();
    }

    public async Task<MetadataDefinitionDto?> GetDefinitionByKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var entity = await db.TblMetadataDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.MetadataKey == key);

        return entity != null ? ToDto(entity) : null;
    }

    public async Task<MetadataDefinitionDto> CreateDefinitionAsync(MetadataDefinitionDto dto, string editUser)
    {
        if (string.IsNullOrWhiteSpace(dto.MetadataKey))
            throw new ArgumentException("Metadata key is required");
        ValidateFieldType(dto.FieldType);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var exists = await db.TblMetadataDefinitions
            .AnyAsync(d => d.MetadataKey == dto.MetadataKey);

        if (exists)
            throw new InvalidOperationException($"A metadata key '{dto.MetadataKey}' already exists");

        var entity = new TblMetadataDefinition
        {
            MetadataKey = dto.MetadataKey.Trim(),
            DisplayName = dto.DisplayName.Trim(),
            FieldType = dto.FieldType,
            OptionsJson = dto.Options.Count > 0 ? JsonSerializer.Serialize(dto.Options) : null,
            Description = dto.Description?.Trim(),
            SortOrder = dto.SortOrder,
            CreateDate = DateTime.UtcNow,
            LastEditDate = DateTime.UtcNow,
            LastEditUser = editUser
        };

        db.TblMetadataDefinitions.Add(entity);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created metadata definition '{Key}' (type: {Type}) by {User}", dto.MetadataKey, dto.FieldType, editUser);
        return ToDto(entity);
    }

    public async Task UpdateDefinitionAsync(int definitionId, MetadataDefinitionDto dto, string editUser)
    {
        ValidateFieldType(dto.FieldType);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var entity = await db.TblMetadataDefinitions.FindAsync(definitionId)
            ?? throw new InvalidOperationException($"Metadata definition {definitionId} not found");

        entity.DisplayName = dto.DisplayName.Trim();
        entity.FieldType = dto.FieldType;
        entity.OptionsJson = dto.Options.Count > 0 ? JsonSerializer.Serialize(dto.Options) : null;
        entity.Description = dto.Description?.Trim();
        entity.SortOrder = dto.SortOrder;
        entity.LastEditDate = DateTime.UtcNow;
        entity.LastEditUser = editUser;

        await db.SaveChangesAsync();
        _logger.LogInformation("Updated metadata definition '{Key}' by {User}", entity.MetadataKey, editUser);
    }

    public async Task<DeleteDefinitionResult> DeleteDefinitionAsync(int definitionId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var entity = await db.TblMetadataDefinitions.FindAsync(definitionId);
        if (entity == null)
            return new DeleteDefinitionResult { Success = false, ErrorMessage = "Definition not found" };

        var usage = await CountUsageAsync(scope, c => c.CountKeyAsync(entity.MetadataKey));

        db.TblMetadataDefinitions.Remove(entity);
        await db.SaveChangesAsync();

        _logger.LogInformation("Deleted metadata definition '{Key}' (tags: {Tags}; references: {References})",
            entity.MetadataKey, usage.DescribeTags() ?? "none", usage.DescribeReferences() ?? "none");
        return new DeleteDefinitionResult { Success = true, Usage = usage };
    }

    public async Task<List<string>> GetDefinedKeysAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        return await db.TblMetadataDefinitions
            .AsNoTracking()
            .OrderBy(d => d.DisplayName)
            .Select(d => d.MetadataKey)
            .ToListAsync();
    }

    public async Task<List<string>> GetOptionsForKeyAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return new List<string>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var optionsJson = await db.TblMetadataDefinitions
            .AsNoTracking()
            .Where(d => d.MetadataKey == key)
            .Select(d => d.OptionsJson)
            .FirstOrDefaultAsync();

        return ParseOptions(optionsJson);
    }

    public async Task<KeyUsageSummary> GetKeyUsageAsync(string key)
    {
        using var scope = _scopeFactory.CreateScope();
        return await CountUsageAsync(scope, c => c.CountKeyAsync(key));
    }

    public async Task<KeyUsageSummary> GetValueUsageAsync(string key, IReadOnlyCollection<string> values)
    {
        if (values.Count == 0) return new KeyUsageSummary();

        using var scope = _scopeFactory.CreateScope();
        return await CountUsageAsync(scope, c => c.CountValuesAsync(key, values));
    }

    // Contributors share the scope's DbContext, so they run one after another.
    private static async Task<KeyUsageSummary> CountUsageAsync(
        IServiceScope scope, Func<IMetadataUsageContributor, Task<IReadOnlyList<MetadataUsageCount>>> count)
    {
        var counts = new List<MetadataUsageCount>();
        foreach (var contributor in scope.ServiceProvider.GetServices<IMetadataUsageContributor>())
            counts.AddRange(await count(contributor));
        return new KeyUsageSummary { Counts = counts };
    }

    private static void ValidateFieldType(string? fieldType)
    {
        if (!MetadataFieldTypes.IsKnown(fieldType))
            throw new ArgumentException(
                $"Unknown field type '{fieldType}'. Use one of: {string.Join(", ", MetadataFieldTypes.All)}.");
    }

    private static MetadataDefinitionDto ToDto(TblMetadataDefinition entity)
    {
        return new MetadataDefinitionDto
        {
            MetadataDefinitionId = entity.MetadataDefinitionId,
            MetadataKey = entity.MetadataKey,
            DisplayName = entity.DisplayName,
            FieldType = entity.FieldType,
            Options = ParseOptions(entity.OptionsJson),
            Description = entity.Description,
            SortOrder = entity.SortOrder
        };
    }

    private static List<string> ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(optionsJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
