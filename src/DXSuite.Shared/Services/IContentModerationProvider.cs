namespace DXSuite.Shared.Services;

/// <summary>
/// Cross-module content moderation abstraction.
/// Returns null when no moderation provider is configured (graceful skip).
/// </summary>
public interface IContentModerationProvider
{
    Task<ModerationResult?> ScanTextAsync(params string[] textInputs);
    Task<ModerationResult?> ScanTextAsync(ModerationContext context, params string[] textInputs);
    Task<ModerationResult?> ScanImageAsync(byte[] imageData, string mimeType, ModerationContext? context = null);
}

public class ModerationContext
{
    public int? UserId { get; set; }
    public int? DisplayId { get; set; }
    public int? LocationId { get; set; }
}

public class ModerationResult
{
    public bool IsInappropriate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string PolicyViolation { get; set; } = string.Empty;
    public List<string> FlaggedTexts { get; set; } = new();
}
