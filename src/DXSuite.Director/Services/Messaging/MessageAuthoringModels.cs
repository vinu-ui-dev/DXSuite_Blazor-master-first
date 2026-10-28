using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// A detached copy of a canned Message being written or edited. The dialog edits only this, so Cancel discards every
/// change; <see cref="MessageAuthoring.SaveAsync"/> is the one place it reaches the database.
/// </summary>
public sealed class MessageDraft
{
    /// <summary>Null for a Message not saved yet.</summary>
    public int? MessageId { get; init; }

    public int MessageTypeId { get; init; }

    public string MessageTypeName { get; init; } = "";

    /// <summary>The type's character limit, counted per language (MSG-02).</summary>
    public int MaxCharacters { get; init; }

    /// <summary>The saved Message's status; null for a new one.</summary>
    public MessageStatus? Status { get; init; }

    /// <summary>True when the type has an approval chain, so a new Message saves as Pending (MSG-12).</summary>
    public bool NeedsApproval { get; init; }

    /// <summary>True when an earlier edit still waits for approval; saving replaces it (MSG-13).</summary>
    public bool HasPendingRevision { get; init; }

    public string Name { get; set; } = "";

    /// <summary>One text per language of the type, in the type's order (MSG-03).</summary>
    public IReadOnlyList<MessageTextDraft> Texts { get; init; } = [];

    /// <summary>The type's Asset Slots in their order (MSG-04).</summary>
    public IReadOnlyList<MessageSlotDraft> Slots { get; init; } = [];

    /// <summary>Where this draft's staged uploads wait until the Message saves; created on the first upload.</summary>
    internal string? StagingFolder { get; set; }
}

/// <summary>The Message's text in one language of its type.</summary>
public sealed class MessageTextDraft
{
    public string LanguageCode { get; init; } = "";

    public string LanguageName { get; init; } = "";

    public string Body { get; set; } = "";

    /// <summary>What the counter shows against the type's limit.</summary>
    public int Count => Body.Length;
}

/// <summary>
/// One Asset Slot of the Message: the asset it holds now, and any upload staged to replace it. A staged upload reaches
/// the asset library only when the Message saves.
/// </summary>
public sealed class MessageSlotDraft
{
    public string SlotKey { get; init; } = "";

    public bool IsRequired { get; init; }

    public int ConstraintId { get; init; }

    /// <summary>The Asset Library location that owns the slot's constraint, where its uploads land.</summary>
    public int? LibraryLocationId { get; init; }

    /// <summary>The constraint's resolution, which the uploader checks a file against.</summary>
    public int? ExpectedWidth { get; init; }

    public int? ExpectedHeight { get; init; }

    public string ResolutionMode { get; init; } = "aspect-ratio";

    /// <summary>The library asset the saved Message holds in this slot, until the draft clears or replaces it.</summary>
    public int? AssetId { get; internal set; }

    public string? AssetName { get; internal set; }

    public StagedUpload? Staged { get; internal set; }

    public bool IsFilled => Staged is not null || AssetId is not null;
}

/// <summary>
/// An uploaded file waiting to join the asset library: <paramref name="DiskPath"/> is the staged copy, outside the
/// folders players read, and <paramref name="FileName"/> the name the user uploaded. <paramref name="UploadingAsUserGroupId"/>
/// is the uploader's "Uploading as" pick, re-checked when the file is ingested.
/// </summary>
public sealed record StagedUpload(string DiskPath, string FileName, int? UploadingAsUserGroupId = null);

public enum MessageRemovalOutcome
{
    Done,

    /// <summary>An Activation of the Message is active or scheduled; it must be cancelled first (MSG-14).</summary>
    Busy,

    /// <summary>The user does not hold <c>feature:messages-author</c>.</summary>
    Forbidden,

    /// <summary>The Message was deleted meanwhile.</summary>
    Gone
}

public enum MessageSaveOutcome
{
    Saved,
    Invalid,

    /// <summary>The user does not hold <c>feature:messages-author</c>.</summary>
    Forbidden,

    /// <summary>The Message or its type was deleted meanwhile.</summary>
    Gone
}

/// <summary>
/// The result of a save. <paramref name="Errors"/> is keyed by <see cref="MessageAuthoring.NameField"/>,
/// <see cref="MessageAuthoring.TextField"/> and <see cref="MessageAuthoring.SlotField"/>.
/// </summary>
public sealed record MessageSaveResult(
    MessageSaveOutcome Outcome,
    int? MessageId = null,
    MessageStatus? Status = null,
    IReadOnlyDictionary<string, string>? Errors = null);
