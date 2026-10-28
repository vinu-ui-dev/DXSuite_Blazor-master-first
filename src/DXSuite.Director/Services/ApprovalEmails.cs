using System.Globalization;
using System.Net;
using System.Text;
using DXSuite.Director.Entities;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXSuite.Director.Services;

/// <summary>
/// Approval emails for every chain, location or Message Type (FSD 1.1 section 12 point 7): when a chain has
/// <see cref="TblApprovalChain.EmailApprovers"/> on, each submission and each advance to a new stage emails that
/// stage's approvers, and approvers get one reminder digest at the chain's interval for whatever still waits on them.
/// Approvers are the enabled members of the groups whose decision is still pending; their sign-in name (UPN) is the
/// only address Director holds. Nothing is sent when the tenant has no SMTP (A-27), and a failed send never fails the
/// approval that caused it.
/// </summary>
public sealed class ApprovalEmails
{
    public const string DigestSubject = "Items still waiting for your approval";

    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IEmailService _email;
    private readonly ILogger _logger;

    public ApprovalEmails(IDbContextFactory<DirectorDbContext> dbFactory, IEmailService email, ILogger<ApprovalEmails>? logger = null)
    {
        _dbFactory = dbFactory;
        _email = email;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>
    /// Emails each approver of the request's current stage once: called after a submission or an advance commits.
    /// </summary>
    public async Task StageReachedAsync(int approvalRequestId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var waiting = await WaitingAsync(db, approvalRequestId, ct);
            if (waiting.Count == 0 || !await _email.IsConfiguredAsync()) return;

            foreach (var (approver, items) in waiting.GroupBy(w => w.Approver).Select(g => (g.Key, g.Select(w => w.Item).Distinct().ToList())))
            {
                var item = items[0];
                var line = $"{item.Title} is waiting for your approval (stage {item.StageNumber} of {item.StageCount}).";
                await SendAsync(approver, $"Waiting for your approval: {item.Title}", [line], ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not email the approvers of approval request {RequestId}", approvalRequestId);
        }
    }

    /// <summary>
    /// Sends each approver one digest of the requests that have waited on them for at least their chain's interval
    /// since they were asked or last reminded, and records the reminder so no instance sends it again before the next
    /// interval. Returns the number of digests sent. Run under the reminder lease (HOST-04).
    /// </summary>
    public async Task<int> SendDueRemindersAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var waiting = await WaitingAsync(db, approvalRequestId: null, ct);
        if (waiting.Count == 0 || !await _email.IsConfiguredAsync()) return 0;

        var requestIds = waiting.Select(w => w.Item.ApprovalRequestId).Distinct().ToList();
        var reminders = await db.TblApprovalReminders
            .Where(r => requestIds.Contains(r.ApprovalRequestId))
            .ToListAsync(ct);

        var due = waiting
            .GroupBy(w => (w.Approver, w.Item))
            .Select(g =>
            {
                var reminder = reminders.FirstOrDefault(r => r.ApprovalRequestId == g.Key.Item.ApprovalRequestId && r.UserId == g.Key.Approver.UserId);
                // Asked when the stage's entries were made; a resubmission or a new stage asks afresh.
                var asked = g.Min(w => w.AskedUtc);
                var since = reminder is not null && reminder.LastSentUtc > asked ? reminder.LastSentUtc : asked;
                return (g.Key.Approver, g.Key.Item, Reminder: reminder, Due: since.AddHours(g.Key.Item.ReminderIntervalHours) <= nowUtc);
            })
            .Where(x => x.Due)
            .ToList();

        var sent = 0;
        foreach (var digest in due.GroupBy(d => d.Approver))
        {
            var items = digest.OrderBy(d => d.Item.SubmittedUtc).ToList();
            var lines = items.Select(d => $"{d.Item.Title}, stage {d.Item.StageNumber} of {d.Item.StageCount}, submitted {d.Item.SubmittedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC.").ToList();
            if (!await SendAsync(digest.Key, DigestSubject, lines, ct)) continue;

            foreach (var item in items)
            {
                if (item.Reminder is not null) item.Reminder.LastSentUtc = nowUtc;
                else db.TblApprovalReminders.Add(new TblApprovalReminder
                {
                    ApprovalRequestId = item.Item.ApprovalRequestId, UserId = digest.Key.UserId, LastSentUtc = nowUtc
                });
            }
            await db.SaveChangesAsync(ct);
            sent++;
        }
        return sent;
    }

    private async Task<bool> SendAsync(Approver approver, string subject, IReadOnlyList<string> lines, CancellationToken ct)
    {
        const string where = "Open Director, Approvals > My Queue, to approve or decline.";
        var text = new StringBuilder();
        var html = new StringBuilder();
        foreach (var line in lines)
        {
            text.AppendLine(line);
            html.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>");
        }
        text.AppendLine().AppendLine(where);
        html.Append("<p>").Append(WebUtility.HtmlEncode(where)).Append("</p>");

        var result = await _email.SendAsync(new EmailMessage([approver.Address], subject, html.ToString(), text.ToString()), ct);
        if (!result.Succeeded && !result.NotConfigured)
            _logger.LogWarning("The approval email to {Address} was not sent: {Error}", approver.Address, result.Error);
        return result.Succeeded;
    }

    /// <summary>
    /// Every (approver, request) pair still waiting on a pending queue entry of an enabled chain that emails its
    /// approvers, for one request or for all of them.
    /// </summary>
    private static async Task<List<Waiting>> WaitingAsync(DirectorDbContext db, int? approvalRequestId, CancellationToken ct)
    {
        var entries = await db.TblApprovalQueueEntries.AsNoTracking()
            .Where(e => e.Status == "pending"
                && e.ApprovalRequest.Status == "pending"
                && e.ApprovalRequest.ApprovalChain.IsEnabled
                && e.ApprovalRequest.ApprovalChain.EmailApprovers
                && (approvalRequestId == null || e.ApprovalRequestId == approvalRequestId))
            .Select(e => new
            {
                e.ApprovalRequestId,
                e.UserGroupId,
                e.CreatedDate,
                e.ApprovalRequest.SubmittedDate,
                e.ApprovalRequest.ApprovalChain.ReminderIntervalHours,
                StageNumber = e.ApprovalStage.StageOrder + 1,
                StageCount = e.ApprovalRequest.ApprovalChain.Stages.Count,
                MessageName = e.ApprovalRequest.Message!.Name,
                RevisionName = e.ApprovalRequest.Message!.PendingRevision!.Name,
                MessageType = e.ApprovalRequest.Message!.MessageType.DisplayName,
                AssetName = e.ApprovalRequest.AssetApproval!.Asset.AssetName,
                LocationName = e.ApprovalRequest.ApprovalChain.Location!.LocationName,
            })
            .ToListAsync(ct);
        if (entries.Count == 0) return [];

        var groupIds = entries.Select(e => e.UserGroupId).Distinct().ToList();
        var members = await db.TblUserGroupMemberships.AsNoTracking()
            .Where(m => groupIds.Contains(m.UserGroupId) && !m.User.IsDisabled && m.User.UserPrincipalName != null && m.User.UserPrincipalName.Contains("@"))
            .Select(m => new { m.UserGroupId, m.UserId, m.User.UserPrincipalName })
            .ToListAsync(ct);

        return (
            from e in entries
            let item = new WaitingItem(
                e.ApprovalRequestId,
                Title(e.MessageName, e.RevisionName, e.MessageType, e.AssetName, e.LocationName),
                e.StageNumber,
                e.StageCount,
                DateTime.SpecifyKind(e.SubmittedDate, DateTimeKind.Utc),
                e.ReminderIntervalHours)
            from m in members.Where(m => m.UserGroupId == e.UserGroupId)
            select new Waiting(new Approver(m.UserId, m.UserPrincipalName!), item, DateTime.SpecifyKind(e.CreatedDate, DateTimeKind.Utc)))
            .ToList();
    }

    private static string Title(string? message, string? revision, string? type, string? asset, string? location) =>
        message is not null
            ? revision is not null
                ? $"An edit to the {type} Message \"{revision}\""
                : $"The new {type} Message \"{message}\""
            : $"\"{asset ?? "Content"}\" for {location ?? "a location"}";

    private sealed record Approver(int UserId, string Address);

    private sealed record WaitingItem(int ApprovalRequestId, string Title, int StageNumber, int StageCount, DateTime SubmittedUtc, int ReminderIntervalHours);

    private sealed record Waiting(Approver Approver, WaitingItem Item, DateTime AskedUtc);
}
