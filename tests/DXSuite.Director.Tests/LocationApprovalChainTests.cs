using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static DXSuite.Director.Tests.MessageAccessDatabase;

namespace DXSuite.Director.Tests;

// Section numbers refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// A location's chain as the shared stage editor saves it through <see cref="ApprovalChainService"/>: the approval
/// email switch and reminder interval apply to every chain (section 12 point 7), and a save replaces the stages.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LocationApprovalChainTests : IClassFixture<MessageAccessDatabase>, IAsyncLifetime
{
    private readonly MessageAccessDatabase _db;

    public LocationApprovalChainTests(MessageAccessDatabase db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [LocalDbFact]
    public async Task A_location_chain_stores_the_email_switch_and_reminder_interval_and_a_resave_replaces_its_stages()
    {
        var duty = await _db.AddGroupAsync("Duty Managers", Ana);

        await using (var db = _db.Director())
            await Service(db).CreateOrUpdateChainAsync(Terminal1, new ApprovalChainDraft
            {
                IsEnabled = true, EmailApprovers = true, ReminderIntervalHours = 8,
                Stages = [new StageDefinition { StageOrder = 0, GroupIds = [Rmu] }],
            }, autoApproveMembers: false, "1");
        await using (var db = _db.Director())
            await Service(db).CreateOrUpdateChainAsync(Terminal1, new ApprovalChainDraft
            {
                IsEnabled = true, EmailApprovers = true, ReminderIntervalHours = 8,
                Stages = [new StageDefinition { StageOrder = 0, GroupIds = [duty] }, new StageDefinition { StageOrder = 1, GroupIds = [Rmu] }],
            }, autoApproveMembers: false, "1");

        await using var check = _db.Director();
        var chain = await check.TblApprovalChains.Include(c => c.Stages).ThenInclude(s => s.StageGroups).SingleAsync();
        Assert.Equal(((int?)Terminal1, (int?)null, true, 8), (chain.LocationId, chain.MessageTypeId, chain.EmailApprovers, chain.ReminderIntervalHours));
        Assert.Equal([[duty], [Rmu]], chain.Stages.OrderBy(s => s.StageOrder).Select(s => s.StageGroups.Select(g => g.UserGroupId).ToArray()));
    }

    // The location path clamps a reminder interval below the minimum, as the Message Type path refuses one.
    [LocalDbFact]
    public async Task A_location_chain_keeps_its_reminder_interval_at_least_the_minimum()
    {
        await using (var db = _db.Director())
            await Service(db).CreateOrUpdateChainAsync(Terminal1, new ApprovalChainDraft
            {
                IsEnabled = true, ReminderIntervalHours = 0, DeclinedContentRetentionDays = 500,
                Stages = [new StageDefinition { StageOrder = 0, GroupIds = [Rmu] }],
            }, autoApproveMembers: true, "1");

        await using var check = _db.Director();
        var chain = await check.TblApprovalChains.SingleAsync();
        Assert.Equal((TblApprovalChain.ReminderIntervalHoursMinimum, 90, true), (chain.ReminderIntervalHours, chain.DeclinedContentRetentionDays, chain.AutoApproveMembers));
    }

    private static ApprovalChainService Service(DirectorDbContext db) =>
        new(db, null!, null!, NullLogger<ApprovalChainService>.Instance, null!, new SilentInvalidator());

    private sealed class SilentInvalidator : IPublicationInvalidator
    {
        public void DisplayChanged(int displayId) { }
        public void DisplayScheduleChanged(int displayId) { }
        public void DisplayRemoved(int displayId) { }
        public void LocationChanged(int locationId) { }
        public void AssetsChanged(IEnumerable<int> locationIds) { }
    }
}
