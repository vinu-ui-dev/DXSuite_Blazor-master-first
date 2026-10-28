using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// A detached copy of a Message Type that the dialog edits. Nothing reaches the database until
/// <see cref="MessageTypeConfiguration.SaveAsync"/>, so closing the dialog discards every change (G-31).
/// <see cref="Languages"/> and <see cref="Slots"/> are kept in their saved order.
/// </summary>
public sealed class MessageTypeDraft
{
    public const int DefaultDisplaySeconds = 10;
    public const int DefaultHideSeconds = 30;
    public const string DefaultTextColour = "#FFFFFF";
    public const string DefaultBackgroundColour = "#000000";

    /// <summary>Null until the first save.</summary>
    public int? MessageTypeId { get; init; }

    /// <summary>The API and CUPPS use the Code, so it cannot change after the first save (MSG-02).</summary>
    public bool CodeLocked => MessageTypeId is not null;

    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public MessageDisplayMode ScreenPart { get; set; }
    public int MaxCharacters { get; set; } = TblMessageType.MaxCharactersDefault;
    public bool IsTopPriority { get; set; }
    public int DisplaySeconds { get; set; } = DefaultDisplaySeconds;
    public int HideSeconds { get; set; } = DefaultHideSeconds;
    public string TextColour { get; set; } = DefaultTextColour;
    public string BackgroundColour { get; set; } = DefaultBackgroundColour;

    /// <summary>Language codes in the order every Message of the type is written and shown.</summary>
    public List<string> Languages { get; init; } = [];

    public List<AssetSlotDraft> Slots { get; init; } = [];

    /// <summary>
    /// Position in the rank order (1 = wins) and the number of ranked types, for display only. 0 for the built-in
    /// Counter type under D-02, which is not ranked (MSG-20).
    /// </summary>
    public int Rank { get; init; }
    public int RankOf { get; init; }

    /// <summary>Canned Messages of this type that delete would hide with it.</summary>
    public int MessageCount { get; init; }

    /// <summary>A Message of the type is on screen or scheduled, so the type cannot be deleted yet.</summary>
    public bool HasLiveActivations { get; init; }

    /// <summary>
    /// The Counter type under D-02: it cannot be deleted (DEV-11), and it sits outside the ranking, so it cannot be
    /// Top priority (MSG-20).
    /// </summary>
    public bool IsBuiltIn { get; init; }
}

/// <summary>One Asset Slot on a draft (MSG-04).</summary>
public sealed class AssetSlotDraft
{
    public string Key { get; set; } = "";
    public int ConstraintId { get; set; }
    public bool IsRequired { get; set; }

    /// <summary>The constraint the slot was saved with, so a legacy binding is kept without being offered again.</summary>
    public int? SavedConstraintId { get; init; }
}

/// <summary>
/// A row of the Message Types list, in rank order. <see cref="Rank"/> is null for the built-in Counter type under D-02,
/// which follows the ranked types (MSG-20).
/// </summary>
public sealed record MessageTypeRow(
    int MessageTypeId,
    string Code,
    string DisplayName,
    int? Rank,
    bool IsTopPriority,
    MessageDisplayMode ScreenPart,
    int MaxCharacters,
    string TextColour,
    string BackgroundColour,
    IReadOnlyList<MessageLanguage> Languages,
    IReadOnlyList<AssetSlotSummary> Slots);

public sealed record AssetSlotSummary(string Key, bool IsRequired);

/// <summary>A constraint an Asset Slot can be bound to: one that has an asset library to draw from.</summary>
public sealed record SlotConstraint(int ConstraintId, string Name, string Library);

/// <summary>What the dialog offers: languages, slot constraints, and the D-02 limits in force.</summary>
public sealed record MessageTypeDialogOptions(
    IReadOnlyList<MessageLanguage> Languages,
    IReadOnlyList<SlotConstraint> Constraints,
    bool LanguagesFixed,
    bool OneAslSlotOnly);

public enum MessageTypeSaveOutcome
{
    Saved,
    Invalid,

    /// <summary>Another type holds Top priority; save again with the move confirmed (MSG-08).</summary>
    ConfirmTopPriorityMove,

    /// <summary>The type was deleted while the dialog was open.</summary>
    Gone
}

/// <param name="Errors">Field name (a <see cref="MessageTypeDraft"/> property) to the message to show under it.</param>
public sealed record MessageTypeSaveResult(
    MessageTypeSaveOutcome Outcome,
    int? MessageTypeId = null,
    IReadOnlyDictionary<string, string>? Errors = null,
    string? TopPriorityHolder = null)
{
    public IReadOnlyDictionary<string, string> Errors { get; } = Errors ?? new Dictionary<string, string>();
}

public enum MessageTypeDeleteOutcome
{
    Deleted,

    /// <summary>A Message of the type is on screen or scheduled; cancel it first.</summary>
    Busy,
    Gone,

    /// <summary>The built-in Counter type under D-02 (DEV-11).</summary>
    BuiltIn
}
