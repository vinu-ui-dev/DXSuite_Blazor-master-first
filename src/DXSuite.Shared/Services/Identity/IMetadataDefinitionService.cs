namespace DXSuite.Shared.Services.Identity;

public interface IMetadataDefinitionService
{
    Task<List<MetadataDefinitionDto>> GetAllDefinitionsAsync();
    Task<MetadataDefinitionDto?> GetDefinitionByKeyAsync(string key);
    Task<MetadataDefinitionDto> CreateDefinitionAsync(MetadataDefinitionDto dto, string editUser);
    Task UpdateDefinitionAsync(int definitionId, MetadataDefinitionDto dto, string editUser);
    /// <summary>
    /// Deletes a key whether or not anything uses it. Deleting is runtime-inert: tags and references stay
    /// and keep working, and what is lost is the display name, the option list and the ability to pick
    /// the key. Callers warn with <see cref="GetKeyUsageAsync"/> first.
    /// </summary>
    Task<DeleteDefinitionResult> DeleteDefinitionAsync(int definitionId);
    Task<List<string>> GetDefinedKeysAsync();
    Task<List<string>> GetOptionsForKeyAsync(string key);
    /// <summary>Every module's tags and references using the key.</summary>
    Task<KeyUsageSummary> GetKeyUsageAsync(string key);

    /// <summary>Every module's tags and references holding any of <paramref name="values"/> for the key.</summary>
    Task<KeyUsageSummary> GetValueUsageAsync(string key, IReadOnlyCollection<string> values);
}

public class MetadataDefinitionDto
{
    public int MetadataDefinitionId { get; set; }
    public string MetadataKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FieldType { get; set; } = "string";
    public List<string> Options { get; set; } = new();
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public class DeleteDefinitionResult
{
    public bool Success { get; set; }
    public KeyUsageSummary? Usage { get; set; }
    public string? ErrorMessage { get; set; }
}
