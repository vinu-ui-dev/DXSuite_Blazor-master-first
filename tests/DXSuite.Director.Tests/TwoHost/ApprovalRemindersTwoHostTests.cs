using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Director.Services.Messaging;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Multitenancy;
using IEmailService = DXSuite.Shared.Services.IEmailService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Tests.TwoHost;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// B-34's done-when on two instances (HOST-04): both Director instances run the approval reminder job against one
/// database and one mail server, and each approver still gets one digest per interval.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "TwoHost")]
public sealed class ApprovalRemindersTwoHostTests
{
    private const int Ivy = 501, Kim = 503;
    private const string IvyMail = "ivy@gtaa.test", KimMail = "kim@gtaa.test";

    [LocalDbFact]
    public async Task Two_hosts_send_each_approver_one_digest_per_interval()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
        var mail = new RecordingEmailService();
        await using var hosts = await TwoHosts.StartAsync(o =>
        {
            o.SeedAsync = db => SeedWaitingRevisionAsync(db, clock.GetUtcNow().UtcDateTime);
            o.ConfigureServices = (_, services, _) =>
            {
                services.AddSingleton<IWorkerScopeFactory, OneTenantOnTheSharedDatabase>();
                services.AddSingleton<IEmailService>(mail);
                services.AddScoped<ApprovalEmails>();
                // Both hosts start at once and keep polling, as two nodes restarted together would.
                services.AddSingleton(sp => new ApprovalReminderService(
                    sp.GetRequiredService<IWorkerScopeFactory>(),
                    sp.GetRequiredService<IDistributedLockService>(),
                    clock,
                    sp.GetRequiredService<ILogger<ApprovalReminderService>>(),
                    startupDelay: TimeSpan.Zero,
                    pollInterval: TimeSpan.FromMilliseconds(200)));
                services.AddHostedService(sp => sp.GetRequiredService<ApprovalReminderService>());
            };
        });

        clock.Advance(TimeSpan.FromHours(1));
        await TwoHosts.WaitForAsync(() => mail.Sent.Count >= 2);
        await BothHostsRunACycleAsync(hosts);
        Assert.Equal([ApprovalEmails.DigestSubject], mail.SubjectsTo(IvyMail));
        Assert.Equal([ApprovalEmails.DigestSubject], mail.SubjectsTo(KimMail));

        clock.Advance(TimeSpan.FromHours(1));
        await TwoHosts.WaitForAsync(() => mail.Sent.Count >= 4);
        await BothHostsRunACycleAsync(hosts);
        Assert.Equal(2, mail.SubjectsTo(IvyMail).Count);
        Assert.Equal(2, mail.SubjectsTo(KimMail).Count);
        Assert.Equal(4, mail.Sent.Count);
    }

    // Another cycle on both hosts at the same moment, on top of the ones their timers keep running.
    private static Task BothHostsRunACycleAsync(TwoHosts hosts) =>
        Task.WhenAll(hosts.Hosts.Select(h => h.Services.GetRequiredService<ApprovalReminderService>().RunCycleAsync(CancellationToken.None)));

    /// <summary>An Approved Message whose edit waits on a one-stage chain of Ivy and Kim, emailed hourly.</summary>
    private static async Task SeedWaitingRevisionAsync(DirectorDbContext db, DateTime now)
    {
        await db.Database.ExecuteSqlRawAsync($"""
            ALTER TABLE tblUser NOCHECK CONSTRAINT ALL;
            SET IDENTITY_INSERT tblUser ON;
            INSERT INTO tblUser (UserId, RoleId, FriendlyName, UserPrincipalName, PulseAccess, IsDisabled)
            VALUES ({Ivy}, 3, 'Ivy Approver', '{IvyMail}', 0, 0), ({Kim}, 3, 'Kim Approver', '{KimMail}', 0, 0);
            SET IDENTITY_INSERT tblUser OFF;
            """);
        var group = new TblUserGroup { GroupName = "Duty", IsActive = true };
        db.TblUserGroups.Add(group);
        var type = new TblMessageType
        {
            Code = "REMINDERS", DisplayName = "Reminder test", Priority = 99, DisplayMode = MessageDisplayMode.Overlay,
            DefaultDisplaySeconds = 10, DefaultHideSeconds = 30, DefaultTextColor = "#FFFFFF", DefaultBackgroundColor = "#000000",
            Languages = [new TblMessageTypeLanguage { LanguageCode = "eng", SortOrder = 1 }]
        };
        db.TblMessageTypes.Add(type);
        await db.SaveChangesAsync();
        db.TblUserGroupMemberships.AddRange(
            new TblUserGroupMembership { UserGroupId = group.UserGroupId, UserId = Ivy, AddedDate = now },
            new TblUserGroupMembership { UserGroupId = group.UserGroupId, UserId = Kim, AddedDate = now });
        db.TblApprovalChains.Add(new TblApprovalChain
        {
            MessageTypeId = type.MessageTypeId, IsEnabled = true, EmailApprovers = true, ReminderIntervalHours = 1, CreateDate = now,
            Stages = [new TblApprovalStage { StageOrder = 0, IntraStageOperator = "OR", StageGroups = [new TblApprovalStageGroup { UserGroupId = group.UserGroupId }] }]
        });
        var message = new TblMessage
        {
            MessageTypeId = type.MessageTypeId, Name = "Gate change", IsCanned = true, Status = MessageStatus.Approved, CreatedUtc = now,
            Texts = [new TblMessageText { LanguageCode = "eng", Body = "Gate changed" }],
            PendingRevision = new TblMessageRevision
            {
                Name = "Gate change", SubmittedUtc = now, Texts = [new TblMessageRevisionText { LanguageCode = "eng", Body = "Gate changed to A12" }]
            }
        };
        db.TblMessages.Add(message);
        await db.SaveChangesAsync();

        await MessageApprovals.SubmitAsync(db, message.MessageId, type.MessageTypeId, now, default);
    }

    /// <summary>The hosts have no tenant registry: one tenant whose scope is the host's own, on the shared database.</summary>
    private sealed class OneTenantOnTheSharedDatabase(IServiceScopeFactory scopes) : IWorkerScopeFactory
    {
        public IReadOnlyList<string> WorkerTenants => ["gtaa"];

        public Task<IServiceScope> CreateTenantScopeAsync(string subdomain, CancellationToken ct = default) =>
            Task.FromResult(scopes.CreateScope());
    }
}
