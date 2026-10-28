using DXSuite.Director.Models;

namespace DXSuite.Director.Services;

public class UploadTask
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Internal storage filename (may include GUID prefix). Used for file path operations.</summary>
    public string FileName { get; set; } = "";

    /// <summary>User-facing display name shown in the minimized bar toast.</summary>
    public string DisplayName { get; set; } = "";

    public int LocationId { get; set; }

    /// <summary>
    /// Lifecycle stage: uploading | processing | complete | failed | cancelled
    /// </summary>
    public string Stage { get; set; } = "uploading";

    public int ProgressPercent { get; set; }
    public string StatusMessage { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    /// <summary>
    /// True briefly after stage changes to "complete" or "failed" to trigger the bar flash animation.
    /// Cleared automatically after the animation duration.
    /// </summary>
    public bool NeedsAttention { get; set; }

    /// <summary>
    /// True for ~500ms before auto-removal so the bar entry can fade out via CSS transition.
    /// </summary>
    public bool IsAutoRemoving { get; set; }

    /// <summary>
    /// The original upload config stored so the modal can be fully restored after navigation away.
    /// </summary>
    public MediaUploadConfig? ResumeConfig { get; set; }

    /// <summary>Web path to the file in TempFiles, for preview on resume.</summary>
    public string? TempWebPath { get; set; }

    /// <summary>Disk path to the file in TempFiles, for submission on resume.</summary>
    public string? TempDiskPath { get; set; }

    /// <summary>
    /// The location's content metadata field keys, saved at Stage 1 so they can be restored if the
    /// user navigates away and resumes via the minimized bar.
    /// </summary>
    public List<string>? MetadataFieldKeys { get; set; }

    /// <summary>
    /// User-entered values for each custom metadata field, saved at Stage 1 so they
    /// survive navigation away and are pre-populated when the modal is resumed.
    /// </summary>
    public Dictionary<string, List<string>>? CustomMetadataValues { get; set; }
}
