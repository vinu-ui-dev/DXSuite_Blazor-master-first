using DXSuite.Director.Entities;
using DXSuite.Director.Services.ContentSource;
using DXSuite.Director.Services.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static DXSuite.Director.Tests.MessageApprovalWorld;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// A rejected or deleted Message deletes its ASL video from the ASL videos library (B-83, INK-10), so the file leaves the
/// media share and, through the Ink content sync, the Ink backup. Driven through <see cref="MessageApprovals"/> and
/// <see cref="MessageAuthoring"/> against a Director database on LocalDB, with the media root in a temporary folder.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AslVideoRemovalTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;
    private readonly AuthoringWorld _authoring;
    private readonly MessageApprovalWorld _world;

    public AslVideoRemovalTests(MessageTypeDatabase db)
    {
        _db = db;
        _authoring = new AuthoringWorld(db);
        _world = new MessageApprovalWorld(db, _authoring);
    }

    public async Task InitializeAsync()
    {
        await _db.ResetAsync();
        if (LocalDbFactAttribute.IsAvailable) await _world.SeedApproversAsync();
    }

    public Task DisposeAsync()
    {
        _authoring.Dispose();
        return Task.CompletedTask;
    }

    // INK-10, B-83 "Done when": a rejected Message leaves no file on the share.
    [LocalDbFact]
    public async Task Rejecting_a_new_Message_deletes_its_ASL_video_from_the_library_and_the_media_root()
    {
        var type = await TypeWithAslSlotAsync(chain: true);
        var message = await NewMessageWithVideoAsync(type, "Evacuate", "evacuate.mp4");
        var (assetId, file) = await AslVideoAsync(message);
        Assert.True(File.Exists(file));

        var item = await _world.OnlyQueueItemAsync(Ivy);
        Assert.Equal(MessageDecisionOutcome.Rejected, (await _world.Approvals(Ivy).RejectAsync(item.QueueEntryId, "Wrong sign", default)).Outcome);

        await using (var db = _db.Director())
        {
            Assert.Equal(MessageStatus.Draft, (await db.TblMessages.SingleAsync(m => m.MessageId == message)).Status);
            Assert.False(await db.TblAssets.AnyAsync(a => a.AssetId == assetId));
            Assert.False(await db.TblMessageAssets.AnyAsync());
        }
        Assert.Equal(1, await Deletes().SweepAsync(default));
        Assert.False(File.Exists(file));
    }

    // INK-10, MSG-13: a rejected revision's new video goes; the approved video screens show stays.
    [LocalDbFact]
    public async Task Rejecting_a_revision_deletes_the_video_it_brought_and_keeps_the_approved_one()
    {
        var type = await TypeWithAslSlotAsync(chain: false);
        var message = await NewMessageWithVideoAsync(type, "Evacuate", "evacuate.mp4");
        var (approvedVideo, approvedFile) = await AslVideoAsync(message);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        await EditVideoAsync(message, "evacuate-v2.mp4");
        int proposedVideo;
        await using (var db = _db.Director())
            proposedVideo = (await db.TblMessageRevisionAssets.SingleAsync()).AssetId;

        var item = await _world.OnlyQueueItemAsync(Ivy);
        await _world.Approvals(Ivy).RejectAsync(item.QueueEntryId, null, default);

        await using (var db = _db.Director())
        {
            Assert.Equal(approvedVideo, (await db.TblMessageAssets.SingleAsync()).AssetId);
            Assert.Equal([approvedVideo], await db.TblAssets.Select(a => a.AssetId).ToListAsync());
            Assert.NotEqual(approvedVideo, proposedVideo);
        }
        Assert.Equal(1, await Deletes().SweepAsync(default));
        Assert.True(File.Exists(approvedFile));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(approvedFile)!));
    }

    // INK-10, MSG-14: deleting a Message deletes its ASL video, and the video of an edit still waiting for approval, whose
    // request is withdrawn; retiring keeps the video with the Message's history.
    [LocalDbFact]
    public async Task Deleting_a_Message_deletes_its_ASL_videos_and_retiring_one_keeps_it()
    {
        var type = await TypeWithAslSlotAsync(chain: false);
        var deleted = await NewMessageWithVideoAsync(type, "Evacuate", "evacuate.mp4");
        var retired = await NewMessageWithVideoAsync(type, "Shelter", "shelter.mp4");
        var (_, deletedFile) = await AslVideoAsync(deleted);
        var (retiredVideo, retiredFile) = await AslVideoAsync(retired);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        await EditVideoAsync(deleted, "evacuate-v2.mp4");

        Assert.Equal(MessageRemovalOutcome.Done, await _world.Authors().DeleteAsync(deleted, default));
        Assert.Equal(MessageRemovalOutcome.Done, await _world.Authors().RetireAsync(retired, default));

        await using (var db = _db.Director())
        {
            Assert.Equal([retiredVideo], await db.TblAssets.Select(a => a.AssetId).ToListAsync());
            Assert.False(await db.TblMessageRevisions.AnyAsync());
            Assert.Equal("withdrawn", await db.TblApprovalRequests.Where(r => r.MessageId == deleted).Select(r => r.Status).SingleAsync());
        }
        Assert.Equal(2, await Deletes().SweepAsync(default));
        Assert.False(File.Exists(deletedFile));
        Assert.True(File.Exists(retiredFile));
        Assert.Empty(await _world.Approvals(Ivy).MyQueueAsync(default));
    }

    // INK-10, INK-16: an approved revision that replaces the ASL video deletes the old one from the library in the
    // approval's transaction; its file stays for the grace period, since a delivery cell may still name it.
    [LocalDbFact]
    public async Task Approving_a_revision_that_replaces_the_ASL_video_deletes_the_old_one_after_the_grace_period()
    {
        var type = await TypeWithAslSlotAsync(chain: false);
        var message = await NewMessageWithVideoAsync(type, "Evacuate", "evacuate.mp4");
        var (oldVideo, oldFile) = await AslVideoAsync(message);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        await EditVideoAsync(message, "evacuate-v2.mp4");

        var item = await _world.OnlyQueueItemAsync(Ivy);
        Assert.Equal(MessageDecisionOutcome.Approved, (await _world.Approvals(Ivy).ApproveAsync(item.QueueEntryId, null, default)).Outcome);

        await AssertOldVideoLeavesAfterTheGracePeriodAsync(message, oldVideo, oldFile);
    }

    // INK-10, INK-16: with no chain the save is the approval, so it deletes the video it replaces the same way.
    [LocalDbFact]
    public async Task A_no_chain_edit_that_replaces_the_ASL_video_deletes_the_old_one_after_the_grace_period()
    {
        var type = await TypeWithAslSlotAsync(chain: false);
        var message = await NewMessageWithVideoAsync(type, "Evacuate", "evacuate.mp4");
        var (oldVideo, oldFile) = await AslVideoAsync(message);

        await EditVideoAsync(message, "evacuate-v2.mp4");

        await AssertOldVideoLeavesAfterTheGracePeriodAsync(message, oldVideo, oldFile);
    }

    private async Task AssertOldVideoLeavesAfterTheGracePeriodAsync(int message, int oldVideo, string oldFile)
    {
        var (newVideo, newFile) = await AslVideoAsync(message);
        Assert.NotEqual(oldVideo, newVideo);
        await using (var db = _db.Director())
            Assert.Equal([newVideo], await db.TblAssets.Select(a => a.AssetId).ToListAsync());

        Assert.Equal(0, await Deletes().SweepAsync(default));
        Assert.True(File.Exists(oldFile));

        _world.Clock.Advance(TimeSpan.FromMinutes(new ContentSourceOptions().ReplacedFileGraceMinutes));
        Assert.Equal(1, await Deletes().SweepAsync(default));
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    private async Task<int> TypeWithAslSlotAsync(bool chain)
    {
        var type = await _db.AddTypeAsync("EMERGENCY", priority: 1);
        await _db.AddSlotAsync(type, MessageTypeConfiguration.AslSlotKey, await _db.AddLibraryConstraintAsync("ASL videos"), required: false);
        if (chain) await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        return type;
    }

    private async Task<int> NewMessageWithVideoAsync(int type, string name, string video)
    {
        var authoring = _world.Authors();
        var draft = (await authoring.NewDraftAsync(type, default))!;
        draft.Name = name;
        draft.Texts[0].Body = name + " now";
        await authoring.StageAsync(draft, MessageTypeConfiguration.AslSlotKey, new StagedUpload(_authoring.UploadedFile(video), video));
        return (await authoring.SaveAsync(draft, default)).MessageId!.Value;
    }

    private async Task EditVideoAsync(int message, string video)
    {
        var authoring = _world.Authors();
        var draft = (await authoring.OpenAsync(message, default))!;
        await authoring.StageAsync(draft, MessageTypeConfiguration.AslSlotKey, new StagedUpload(_authoring.UploadedFile(video), video));
        Assert.Equal(MessageSaveOutcome.Saved, (await authoring.SaveAsync(draft, default)).Outcome);
    }

    private async Task<(int AssetId, string File)> AslVideoAsync(int message)
    {
        await using var db = _db.Director();
        var asset = (await db.TblMessageAssets.Include(a => a.Asset).SingleAsync(a => a.MessageId == message)).Asset;
        return (asset.AssetId, Path.Combine(_authoring.MediaRoot, asset.AssetPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
    }

    private PendingFileDeletes Deletes() => new(
        _db.Factory, _authoring.MediaPaths, Options.Create(new ContentSourceOptions()), _world.Clock, NullLogger<PendingFileDeletes>.Instance);
}
