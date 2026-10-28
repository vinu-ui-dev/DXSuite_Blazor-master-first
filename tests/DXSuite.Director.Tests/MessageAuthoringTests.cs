using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.ContentSource;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.CustomerDeviations;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

// Clause and gap ids in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// Message authoring (B-32): what a canned Message must hold before it saves, the status it saves with, and how its
/// Asset Slot uploads are staged, through <see cref="MessageAuthoring"/> against a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageAuthoringTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;
    private readonly AuthoringWorld _world;

    public MessageAuthoringTests(MessageTypeDatabase db)
    {
        _db = db;
        _world = new AuthoringWorld(db);
    }

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync()
    {
        _world.Dispose();
        return Task.CompletedTask;
    }

    [LocalDbFact]
    public async Task A_new_Message_on_a_type_with_no_chain_saves_Approved_with_its_name_and_every_text()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        var authoring = _world.Service();

        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = "Gate change";
        draft.Texts[0].Body = "Gate changed";
        draft.Texts[1].Body = "Porte changée";
        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageSaveOutcome.Saved, result.Outcome);
        await using var db = _db.Director();
        var saved = await db.TblMessages.Include(m => m.Texts).SingleAsync();
        Assert.Equal("Gate change", saved.Name);
        Assert.Equal(MessageStatus.Approved, saved.Status);
        Assert.True(saved.IsCanned);
        Assert.Equal(AuthoringWorld.UserId, saved.CreatedByUserId);
        Assert.Equal(
            [("eng", "Gate changed"), ("fra", "Porte changée")],
            saved.Texts.OrderBy(t => t.LanguageCode).Select(t => (t.LanguageCode, t.Body)));
    }

    [LocalDbFact]
    public async Task A_new_Message_on_a_type_with_an_approval_chain_saves_Pending()
    {
        var type = await _db.AddTypeAsync("EMERGENCY", priority: 1);
        await _db.AddTypeChainAsync(type);
        var authoring = _world.Service();

        var draft = (await authoring.NewDraftAsync(type, default))!;
        Assert.True(draft.NeedsApproval);
        draft.Name = "Evacuate";
        draft.Texts[0].Body = "Leave the building";
        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageStatus.Pending, result.Status);
        await using var db = _db.Director();
        Assert.Equal(MessageStatus.Pending, (await db.TblMessages.SingleAsync()).Status);
    }

    [LocalDbFact]
    public async Task A_chain_saved_from_the_types_Access_dialog_sends_a_new_Message_to_Pending_and_none_approves_it()
    {
        var withChain = await _db.AddTypeAsync("EMERGENCY", priority: 1);
        var withoutChain = await _db.AddTypeAsync("PAGING", priority: 2);
        await using (var db = _db.Director())
            await db.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT 1 FROM tblUserGroup WHERE GroupName = 'Approvers') INSERT INTO tblUserGroup (GroupName, IsActive) VALUES ('Approvers', 1)");
        int approvers;
        await using (var db = _db.Director())
            approvers = await db.TblUserGroups.Where(g => g.GroupName == "Approvers").Select(g => g.UserGroupId).SingleAsync();
        var chains = new MessageApprovalChain(_db.Factory, _world.Session(PermissionKeys.ConfigTabMessageTypes),
            new AccountKindSignInTests.RecordingUserEventLog(), Microsoft.Extensions.Logging.Abstractions.NullLogger<MessageApprovalChain>.Instance,
            TimeProvider.System, new ApprovalEmails(_db.Factory, new RecordingEmailService()));
        await chains.SaveAsync(withChain, new ApprovalChainDraft { IsEnabled = true, Stages = [new StageDefinition { GroupIds = [approvers] }] });
        var authoring = _world.Service();

        Assert.Equal(MessageStatus.Pending, await SaveNewAsync(withChain));
        Assert.Equal(MessageStatus.Approved, await SaveNewAsync(withoutChain));

        async Task<MessageStatus?> SaveNewAsync(int type)
        {
            var draft = (await authoring.NewDraftAsync(type, default))!;
            draft.Name = "Evacuate";
            draft.Texts[0].Body = "Leave the building";
            return (await authoring.SaveAsync(draft, default)).Status;
        }
    }

    [LocalDbFact]
    public async Task A_switched_off_chain_approves_a_new_Message_at_once()
    {
        var type = await _db.AddTypeAsync("EMERGENCY", priority: 1);
        await _db.AddTypeChainAsync(type, enabled: false);
        var authoring = _world.Service();

        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = "Evacuate";
        draft.Texts[0].Body = "Leave the building";

        Assert.Equal(MessageStatus.Approved, (await authoring.SaveAsync(draft, default)).Status);
    }

    [LocalDbFact]
    public async Task A_Message_with_no_name_does_not_save()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var authoring = _world.Service();

        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = "   ";
        draft.Texts[0].Body = "Hello";
        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageSaveOutcome.Invalid, result.Outcome);
        Assert.Contains(MessageAuthoring.NameField, result.Errors!.Keys);
        await using var db = _db.Director();
        Assert.False(await db.TblMessages.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_Message_missing_one_language_does_not_save()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        var authoring = _world.Service();

        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = "Gate change";
        draft.Texts[0].Body = "Gate changed";
        draft.Texts[1].Body = " ";
        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageSaveOutcome.Invalid, result.Outcome);
        Assert.Equal([MessageAuthoring.TextField("fra")], result.Errors!.Keys);
        await using var db = _db.Director();
        Assert.False(await db.TblMessages.AnyAsync());
    }

    [LocalDbFact]
    public async Task Each_language_is_counted_against_the_types_character_limit()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        await _db.SetMaxCharactersAsync(type, 10);
        var authoring = _world.Service();

        var draft = (await authoring.NewDraftAsync(type, default))!;
        Assert.Equal(10, draft.MaxCharacters);
        draft.Name = "Short";
        draft.Texts[0].Body = "0123456789";
        draft.Texts[1].Body = "0123456789A";
        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageSaveOutcome.Invalid, result.Outcome);
        Assert.Equal([MessageAuthoring.TextField("fra")], result.Errors!.Keys);
    }

    [LocalDbFact]
    public async Task A_Message_missing_a_required_slot_does_not_save()
    {
        var (type, _) = await TypeWithSlotAsync(required: true);
        var authoring = _world.Service();

        var draft = await NamedDraftAsync(authoring, type);
        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageSaveOutcome.Invalid, result.Outcome);
        Assert.Equal([MessageAuthoring.SlotField("ASL")], result.Errors!.Keys);
        await using var db = _db.Director();
        Assert.False(await db.TblMessages.AnyAsync());
    }

    [LocalDbFact]
    public async Task An_optional_slot_may_stay_empty()
    {
        var (type, _) = await TypeWithSlotAsync(required: false);
        var authoring = _world.Service();

        var result = await authoring.SaveAsync(await NamedDraftAsync(authoring, type), default);

        Assert.Equal(MessageSaveOutcome.Saved, result.Outcome);
    }

    [LocalDbFact]
    public async Task A_staged_upload_joins_the_slots_asset_library_only_when_the_Message_saves()
    {
        var (type, constraint) = await TypeWithSlotAsync(required: true);
        var authoring = _world.Service();

        var draft = await NamedDraftAsync(authoring, type);
        await authoring.StageAsync(draft, "ASL", new StagedUpload(_world.UploadedFile("sign.mp4", "asl video"), "sign.mp4"));
        Assert.Empty(_world.Library.Ingested);
        Assert.Equal("sign.mp4", draft.Slots[0].Staged!.FileName);

        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageSaveOutcome.Saved, result.Outcome);
        var (request, content) = Assert.Single(_world.Library.Ingested);
        Assert.Equal("asl video", content);
        Assert.Equal(constraint, request.ConstraintId);
        Assert.Equal(await _db.LibraryLocationOfAsync(constraint), request.LocationId);
        Assert.Equal(AuthoringWorld.UserId, request.UploaderUserId);
        Assert.False(request.EnableContentApproval);
        await using var db = _db.Director();
        var link = await db.TblMessageAssets.Include(a => a.Asset).SingleAsync();
        Assert.Equal(("ASL", result.MessageId!.Value), (link.SlotKey, link.MessageId));
        Assert.Equal("sign.mp4", link.Asset.AssetName);
        Assert.Empty(_world.StagedFiles());
    }

    [LocalDbFact]
    public async Task Cancel_after_an_upload_leaves_nothing_in_the_asset_library_or_on_disk()
    {
        var (type, _) = await TypeWithSlotAsync(required: true);
        var authoring = _world.Service();

        var draft = await NamedDraftAsync(authoring, type);
        await authoring.StageAsync(draft, "ASL", new StagedUpload(_world.UploadedFile("sign.mp4"), "sign.mp4"));
        authoring.Discard(draft);

        Assert.Empty(_world.Library.Ingested);
        Assert.Empty(_world.StagedFiles());
        await using var db = _db.Director();
        Assert.False(await db.TblAssets.AnyAsync());
        Assert.False(await db.TblMessages.AnyAsync());
    }

    [LocalDbFact]
    public async Task A_second_upload_to_a_slot_replaces_the_first_staged_file()
    {
        var (type, _) = await TypeWithSlotAsync(required: true);
        var authoring = _world.Service();

        var draft = await NamedDraftAsync(authoring, type);
        await authoring.StageAsync(draft, "ASL", new StagedUpload(_world.UploadedFile("first.mp4", "first"), "first.mp4"));
        await authoring.StageAsync(draft, "ASL", new StagedUpload(_world.UploadedFile("second.mp4", "second"), "second.mp4"));

        Assert.Single(_world.StagedFiles());
        await authoring.SaveAsync(draft, default);
        Assert.Equal("second", Assert.Single(_world.Library.Ingested).Content);
    }

    [LocalDbFact]
    public async Task When_the_library_refuses_an_upload_nothing_saves_and_the_file_stays_staged_for_another_try()
    {
        var (type, _) = await TypeWithSlotAsync(required: true);
        var authoring = _world.Service();
        _world.Library.Fail = true;

        var draft = await NamedDraftAsync(authoring, type);
        await authoring.StageAsync(draft, "ASL", new StagedUpload(_world.UploadedFile("sign.mp4"), "sign.mp4"));
        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageSaveOutcome.Invalid, result.Outcome);
        Assert.Contains(MessageAuthoring.SlotField("ASL"), result.Errors!.Keys);
        Assert.Single(_world.StagedFiles());
        await using var db = _db.Director();
        Assert.False(await db.TblMessages.AnyAsync());

        _world.Library.Fail = false;
        Assert.Equal(MessageSaveOutcome.Saved, (await authoring.SaveAsync(draft, default)).Outcome);
    }

    [LocalDbFact]
    public async Task Only_a_holder_of_messages_author_can_save_a_Message()
    {
        var (type, _) = await TypeWithSlotAsync(required: true);
        var authoring = _world.Service(author: false);

        var draft = await NamedDraftAsync(authoring, type);
        await authoring.StageAsync(draft, "ASL", new StagedUpload(_world.UploadedFile("sign.mp4"), "sign.mp4"));
        var result = await authoring.SaveAsync(draft, default);

        Assert.Equal(MessageSaveOutcome.Forbidden, result.Outcome);
        Assert.Empty(_world.Library.Ingested);
        await using var db = _db.Director();
        Assert.False(await db.TblMessages.AnyAsync());
    }

    [LocalDbFact]
    public async Task Editing_a_Message_changes_nothing_until_it_saves_and_then_saves_in_place()
    {
        var (type, _) = await TypeWithSlotAsync(required: false);
        var authoring = _world.Service();
        var first = await NamedDraftAsync(authoring, type);
        await authoring.StageAsync(first, "ASL", new StagedUpload(_world.UploadedFile("sign.mp4"), "sign.mp4"));
        var id = (await authoring.SaveAsync(first, default)).MessageId!.Value;

        var draft = (await authoring.OpenAsync(id, default))!;
        Assert.Equal("Gate change", draft.Name);
        Assert.Equal("Gate changed", draft.Texts[0].Body);
        Assert.Equal("sign.mp4", draft.Slots[0].AssetName);
        draft.Name = "Renamed";
        draft.Texts[0].Body = "Changed";
        authoring.ClearSlot(draft, "ASL");

        // Nothing reaches the database before the save.
        await using (var before = _db.Director())
        {
            var unchanged = await before.TblMessages.Include(m => m.Texts).Include(m => m.Assets).SingleAsync();
            Assert.Equal(("Gate change", "Gate changed", 1), (unchanged.Name, unchanged.Texts.Single().Body, unchanged.Assets.Count));
        }

        Assert.Equal(MessageSaveOutcome.Saved, (await authoring.SaveAsync(draft, default)).Outcome);
        await using var db = _db.Director();
        var saved = await db.TblMessages.Include(m => m.Texts).Include(m => m.Assets).SingleAsync();
        Assert.Equal(("Renamed", "Changed", 0), (saved.Name, saved.Texts.Single().Body, saved.Assets.Count));
        Assert.Equal(AuthoringWorld.UserId, saved.ModifiedByUserId);
        // Clearing a slot unlinks the asset; the asset stays in its library.
        Assert.True(await db.TblAssets.AnyAsync());
    }

    [LocalDbFact]
    public async Task An_edited_Pending_Message_stays_Pending_under_its_chain()
    {
        var type = await _db.AddTypeAsync("EMERGENCY", priority: 1);
        await _db.AddTypeChainAsync(type);
        var authoring = _world.Service();
        var id = (await authoring.SaveAsync(await NamedDraftAsync(authoring, type), default)).MessageId!.Value;

        var draft = (await authoring.OpenAsync(id, default))!;
        Assert.Equal(MessageStatus.Pending, draft.Status);
        draft.Texts[0].Body = "Gate changed again";

        Assert.Equal(MessageStatus.Pending, (await authoring.SaveAsync(draft, default)).Status);
    }

    [LocalDbFact]
    public async Task A_one_off_Message_is_not_opened_for_authoring()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var oneOff = await _db.AddMessageAsync(type, DateTime.UtcNow, null);
        await _db.MakeOneOffAsync(oneOff);

        Assert.Null(await _world.Service().OpenAsync(oneOff, default));
    }

    // ---- Retire and delete (MSG-14) ----

    [LocalDbFact]
    public async Task Retiring_or_deleting_a_Message_is_refused_while_one_of_its_Activations_is_active_or_scheduled()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var onScreen = await _db.AddMessageAsync(type, DateTime.UtcNow.AddHours(-1), null);
        var scheduled = await _db.AddMessageAsync(type, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2));
        var authoring = _world.Service();

        foreach (var message in new[] { onScreen, scheduled })
        {
            Assert.Equal(MessageRemovalOutcome.Busy, await authoring.RetireAsync(message, default));
            Assert.Equal(MessageRemovalOutcome.Busy, await authoring.DeleteAsync(message, default));
        }

        await using var db = _db.Director();
        Assert.All(await db.TblMessages.ToListAsync(), m => Assert.Equal((MessageStatus.Approved, false, (int?)null),
            (m.Status, m.IsDeleted, m.ModifiedByUserId)));
    }

    [LocalDbFact]
    public async Task A_Message_whose_Activations_have_ended_or_been_cancelled_retires_and_deletes_as_the_author()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var ended = await _db.AddMessageAsync(type, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));
        var cancelled = await _db.AddMessageAsync(type, DateTime.UtcNow.AddHours(-1), null, cancelled: DateTime.UtcNow.AddMinutes(-1));
        var authoring = _world.Service();

        Assert.Equal(MessageRemovalOutcome.Done, await authoring.RetireAsync(ended, default));
        Assert.Equal(MessageRemovalOutcome.Done, await authoring.DeleteAsync(cancelled, default));

        await using var db = _db.Director();
        var retired = await db.TblMessages.SingleAsync(m => m.MessageId == ended);
        Assert.Equal((MessageStatus.Retired, false, (int?)AuthoringWorld.UserId), (retired.Status, retired.IsDeleted, retired.ModifiedByUserId));
        var deleted = await db.TblMessages.SingleAsync(m => m.MessageId == cancelled);
        Assert.Equal((true, (int?)AuthoringWorld.UserId), (deleted.IsDeleted, deleted.ModifiedByUserId));
        Assert.NotNull(deleted.ModifiedUtc);
        // The Message's history stays: its Activations are kept.
        Assert.Equal(2, await db.TblMessageActivations.CountAsync());
        Assert.Null(await authoring.OpenAsync(ended, default));
    }

    [LocalDbFact]
    public async Task Only_an_author_may_retire_or_delete_a_Message()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var message = await _db.AddMessageAsync(type, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));
        var reader = _world.Service(author: false);

        Assert.Equal(MessageRemovalOutcome.Forbidden, await reader.RetireAsync(message, default));
        Assert.Equal(MessageRemovalOutcome.Forbidden, await reader.DeleteAsync(message, default));
        Assert.Equal(MessageRemovalOutcome.Gone, await _world.Service().DeleteAsync(message + 1000, default));
    }

    private async Task<(int Type, int Constraint)> TypeWithSlotAsync(bool required)
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        var constraint = await _db.AddLibraryConstraintAsync("ASL videos");
        await _db.AddSlotAsync(type, "ASL", constraint, required);
        return (type, constraint);
    }

    private static async Task<MessageDraft> NamedDraftAsync(MessageAuthoring authoring, int type)
    {
        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = "Gate change";
        draft.Texts[0].Body = "Gate changed";
        return draft;
    }
}

/// <summary>
/// What <see cref="MessageAuthoring"/> runs against in a test: the fixture's database, a media root of its own, an
/// author session and an asset library that records each upload it keeps.
/// </summary>
internal sealed class AuthoringWorld : IDisposable
{
    public const int UserId = 42;

    private readonly MessageTypeDatabase _db;

    public AuthoringWorld(MessageTypeDatabase db)
    {
        _db = db;
        MediaRoot = Path.Combine(Path.GetTempPath(), "dxme591-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(MediaRoot);
        Library = new RecordingAssetLibrary(db, MediaRoot);
    }

    public string MediaRoot { get; }

    public RecordingAssetLibrary Library { get; }

    /// <summary>The User events the author's saves write, as <see cref="UserId"/>.</summary>
    public RecordingEventLog Events { get; } = new(UserId);

    public ITenantMediaPathResolver MediaPaths => new FixedMediaRoot(MediaRoot);

    public KeySession Session(params string[] keys) => new(keys) { User = new Author() };

    public MessageAuthoring Service(bool author = true, bool gtaa = false)
    {
        var d02 = Options.Create(new D02GtaaMessagingOptions { Enabled = gtaa });
        return new MessageAuthoring(_db.Factory, new MessageLanguageCatalogue(d02, new FakeLanguages()),
            author ? Session(PermissionKeys.MessagesAuthor) : Session(), Library, MediaPaths,
            TimeProvider.System, Events, Versions(), Files(TimeProvider.System));
    }

    /// <summary>The media files queued for deletion under <see cref="MediaRoot"/>, on <paramref name="clock"/>.</summary>
    public PendingFileDeletes Files(TimeProvider clock) =>
        new(_db.Factory, MediaPaths, Options.Create(new ContentSourceOptions()), clock, NullLogger<PendingFileDeletes>.Instance);

    /// <summary>The shown version writer, reading asset files under <see cref="MediaRoot"/>.</summary>
    public ShownVersions Versions() => new(MediaPaths, NullLogger<ShownVersions>.Instance);

    /// <summary>A file as the uploader leaves it staged under TempFiles, before the Message takes it over.</summary>
    public string UploadedFile(string name, string content = "video bytes")
    {
        var folder = Path.Combine(MediaRoot, "TempFiles", "9", "9");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N")[..8] + "_" + name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Every file left anywhere under the media root's TempFiles.</summary>
    public IReadOnlyList<string> StagedFiles() =>
        Directory.Exists(Path.Combine(MediaRoot, "TempFiles"))
            ? Directory.GetFiles(Path.Combine(MediaRoot, "TempFiles"), "*", SearchOption.AllDirectories)
            : [];

    public void Dispose()
    {
        try { Directory.Delete(MediaRoot, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedMediaRoot(string root) : ITenantMediaPathResolver
    {
        public string GetRoot() => root;
    }

    private sealed class Author : ICurrentUser
    {
        public int UserId => AuthoringWorld.UserId;
        public string UserPrincipalName => "author@example.test";
        public string? FriendlyName => "Author";
        public string? GivenName => null;
        public string? Surname => null;
        public bool IsDisabled => false;
        public int RoleId => 7;
        public string? ExtendedData => null;
        public Guid SecurityStamp => Guid.Empty;
    }
}

/// <summary>
/// The asset library as the authoring service sees it: each ingest adds a row to tblAsset, as the real pipeline
/// does, and is recorded with the bytes it was given.
/// </summary>
internal sealed class RecordingAssetLibrary(MessageTypeDatabase db, string mediaRoot) : IAssetIngestService
{
    public List<(AssetIngestRequest Request, string Content)> Ingested { get; } = new();

    public List<int> Deleted { get; } = new();

    public bool Fail { get; set; }

    public async Task<AssetIngestResult> IngestAsync(AssetIngestRequest request, CancellationToken ct = default)
    {
        using var reader = new StreamReader(request.Content);
        var content = await reader.ReadToEndAsync(ct);
        if (Fail) return new AssetIngestResult { Error = "The library refused it." };
        Ingested.Add((request, content));

        var webPath = $"/MediaAssets/{request.LocationId}/{request.ConstraintId}/{request.FileName}";
        var diskPath = Path.Combine(mediaRoot, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(diskPath)!);
        await File.WriteAllTextAsync(diskPath, content, ct);

        await using var director = db.Director();
        var id = await director.Database.SqlQueryRaw<int>("""
            ALTER TABLE tblAsset NOCHECK CONSTRAINT ALL;
            INSERT INTO tblAsset (LocationId, AssetName, AssetPath, AssetGroupId, AssetTypeId, AssetHeight, AssetWidth,
                ConstraintId, Duration, EnableHealthMonitoring, SortOrder, CreateDate, IsDeleted, ContentPackageId, UploadedByUserId)
            OUTPUT INSERTED.AssetId AS Value
            VALUES ({0}, {1}, {2}, 1, 2, 0, 0, {3}, 0, 0, 0, SYSUTCDATETIME(), 0, NEWID(), {4});
            """, request.LocationId, request.FileName, webPath,
            request.ConstraintId, request.UploaderUserId).ToListAsync(ct);
        return new AssetIngestResult { Success = true, AssetId = id.Single() };
    }

    public async Task<bool> DeleteAssetAsync(int assetId, CancellationToken ct = default)
    {
        Deleted.Add(assetId);
        await using var director = db.Director();
        await director.Database.ExecuteSqlRawAsync("DELETE FROM tblAsset WHERE AssetId = {0}", assetId);
        return true;
    }
}
