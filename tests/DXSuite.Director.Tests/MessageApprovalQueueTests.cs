using DXSuite.Director.Components.Approvals;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DXSuite.Director.Tests.MessageApprovalWorld;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// The Messages section of My Queue (B-34, MSG-11, MSG-13) as an approver uses it on the Approvals page, driven through
/// <see cref="InteractiveRenderer"/> over a Director database on LocalDB.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MessageApprovalQueueTests : IClassFixture<MessageTypeDatabase>, IAsyncLifetime
{
    private readonly MessageTypeDatabase _db;
    private readonly AuthoringWorld _authoring;
    private readonly MessageApprovalWorld _world;

    public MessageApprovalQueueTests(MessageTypeDatabase db)
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

    [LocalDbFact]
    public async Task A_pending_revision_shows_beside_the_approved_text_with_every_language_side_by_side_and_the_ASL_video()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1, top: false, deleted: false, "eng", "fra");
        var constraint = await _db.AddLibraryConstraintAsync("ASL videos");
        await _db.AddSlotAsync(type, "ASL", constraint, required: false);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"), ("fra", "Porte changée"));
        var authoring = _world.Authors();
        var draft = (await authoring.OpenAsync(message, default))!;
        draft.Texts[0].Body = "Gate changed to A12";
        await authoring.StageAsync(draft, "ASL", new StagedUpload(_authoring.UploadedFile("sign.mp4"), "sign.mp4"));
        await authoring.SaveAsync(draft, default);
        var entry = (await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId;

        using var queue = await RenderAsync(Ivy);

        var html = queue.Html;
        Assert.Contains("English", html);
        Assert.Contains("French", html);
        Assert.Contains("ASL video", html);
        var approved = Row(html, $"mq-approved-{entry}");
        var proposed = Row(html, $"mq-proposed-{entry}");
        Assert.Contains("Gate changed<", approved);
        Assert.Contains("Porte changée", approved);
        Assert.Contains("None", approved);
        Assert.Contains("Gate changed to A12", proposed);
        Assert.Contains("Porte changée", proposed);
        Assert.Contains("sign.mp4", proposed);
        Assert.Contains("<video", proposed);
        Assert.DoesNotContain("style=", html);
    }

    [LocalDbFact]
    public async Task Approve_makes_the_revision_the_approved_text_and_takes_it_off_the_queue()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "Gate changed to A12");
        var entry = (await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId;
        var counts = new List<int>();

        using var queue = await RenderAsync(Ivy, counts);
        await queue.InputAsync($"#mq-notes-{entry}", "Checked with gates");
        await queue.ClickAsync($"#mq-approve-{entry}");

        Assert.DoesNotContain($"mq-item-{entry}", queue.Html);
        Assert.Contains("The Message is approved.", queue.Html);
        Assert.Equal([1, 0], counts);
        await using var db = _db.Director();
        Assert.Equal("Gate changed to A12", (await db.TblMessageTexts.SingleAsync()).Body);
        Assert.Equal("Checked with gates", (await db.TblApprovalActions.SingleAsync()).Notes);
    }

    [LocalDbFact]
    public async Task Reject_discards_the_revision()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        var message = await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed"));
        await _world.EditAsync(message, "Gate changed to A12");
        var entry = (await _world.OnlyQueueItemAsync(Ivy)).QueueEntryId;

        using var queue = await RenderAsync(Ivy);
        await queue.ClickAsync($"#mq-reject-{entry}");

        Assert.Contains("The Message was rejected.", queue.Html);
        await using var db = _db.Director();
        Assert.Equal("Gate changed", (await db.TblMessageTexts.SingleAsync()).Body);
        Assert.False(await db.TblMessageRevisions.AnyAsync());
    }

    [LocalDbFact]
    public async Task Someone_with_nothing_waiting_sees_no_Messages_section()
    {
        var type = await _db.AddTypeAsync("PAGING", priority: 1);
        await _world.ChainAsync(type, stages: ("OR", [await _world.GroupAsync("Duty", Ivy)]));
        await _world.EditAsync(await _world.ApprovedMessageAsync(type, "Gate change", ("eng", "Gate changed")), "Gate changed to A12");

        using var queue = await RenderAsync(Jon);

        Assert.DoesNotContain("mq-messages", queue.Html);
    }

    // One row of the versions table, from its id to the row's end.
    private static string Row(string html, string id)
    {
        var start = html.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No row {id}.");
        return html[start..html.IndexOf("</tr>", start, StringComparison.Ordinal)];
    }

    private async Task<InteractiveRenderer> RenderAsync(int userId, List<int>? counts = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(_world.Approvals(userId));
        var provider = services.BuildServiceProvider();
        var parameters = new Dictionary<string, object?>();
        if (counts is not null)
            parameters["OnCountChanged"] = Microsoft.AspNetCore.Components.EventCallback.Factory.Create<int>(this, counts.Add);
        return await InteractiveRenderer.RenderAsync<MessageApprovalQueue>(parameters, provider);
    }
}
