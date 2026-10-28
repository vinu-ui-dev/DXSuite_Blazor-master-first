using System.Data;
using DXSuite.Shared.Multitenancy;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DXSuite.Sentinel.Services.Pulse;

/// <summary>How a Pulse call ended. Callers branch on this; the gateway never lets a SQL error escape.</summary>
public enum PulseOutcome
{
    Success,

    /// <summary>The linked DeviceID no longer exists under the customer: a 547 on
    /// <c>FK_tblPulse_tblDevice</c>, or an UPDATE of <c>tblDevice</c> that touched no row.</summary>
    DeviceMissing,

    /// <summary>A 547 on <c>FK_tblPulse_tblMonitoritem</c>: the reading named an item Pulse does not have.</summary>
    MonitorItemMissing,

    /// <summary>Create in Pulse hit an existing (DeviceID, CustomerID).</summary>
    DuplicateDeviceId,

    /// <summary>A reading still collided on <c>PK_tblPulse</c> after the one retry.</summary>
    KeyCollision,

    /// <summary>The tenant has no Pulse connection string or no CustomerId.</summary>
    NotConfigured,

    /// <summary>The Pulse server could not be reached or refused the login.</summary>
    Unreachable,

    /// <summary>Pulse accepted the connection but rejected the statement.</summary>
    Rejected,

    /// <summary>SQL error 229 or 230: the Pulse login lacks a GRANT the statement needs.</summary>
    PermissionDenied,
}

public sealed record PulseResult(PulseOutcome Outcome, string? Error = null)
{
    public bool IsSuccess => Outcome == PulseOutcome.Success;

    public static readonly PulseResult Ok = new(PulseOutcome.Success);
}

public sealed record PulseResult<T>(PulseOutcome Outcome, T? Value, string? Error = null)
{
    public bool IsSuccess => Outcome == PulseOutcome.Success;
}

public sealed record PulseReading(
    string DeviceId,
    int MonitorItemId,
    DateTime StampUtc,
    string Summary,
    int OpenIssueCount,
    DateTime? OldestTriggeredUtc,
    int ErrorLevel);

/// <param name="OtherCustomerIds">Other customers holding the same DeviceID. <c>tblPulse</c>'s key omits
/// CustomerID, so readings for a shared DeviceID can collide.</param>
public sealed record PulseDevice(
    string DeviceId,
    string? Description,
    int Monitor,
    bool Snooze,
    DateTime? SnoozeTimeUtc,
    DateTime? HeartbeatUtc,
    IReadOnlyList<string> OtherCustomerIds)
{
    public bool IsMonitored => Monitor == 1;
}

public sealed record PulseDeviceType(int DeviceTypeId, string Description);

public sealed record PulseMonitorItem(int MonitorItemId, string? Description, int? ItemTypeId);

public sealed record PulseNewDevice(
    string DeviceId,
    string Description,
    int DeviceTypeId,
    bool Monitor,
    DateTime? HeartbeatUtc);

/// <summary>Where a candidate DeviceID already lives. <c>tblPulse</c>'s key omits CustomerID,
/// so a DeviceID shared with another customer can collide on readings.</summary>
public sealed record PulseDeviceIdUsage(bool ExistsForCustomer, IReadOnlyList<string> OtherCustomerIds);

/// <summary>
/// The only code that touches a customer's Pulse database. Pulse's schema is owned by Pulse,
/// so every statement is hand-written parameterized SQL with no EF model; the statements are
/// the write contract in docs/spec/sentinel-pulse-reporting.md section 3 and must not widen.
/// Deciding what to write belongs to the publish planner, not here.
/// </summary>
public sealed class PulseGateway
{
    public const int MaxDeviceIdLength = 50;
    public const int MaxDescriptionLength = 150;
    private const string WriterName = "Sentinel";

    private const int ForeignKeyViolation = 547;
    private const int PrimaryKeyViolation = 2627;
    private const int ObjectPermissionDenied = 229;

    // A login granted UPDATE on some tblDevice columns but not the one being written gets 230, not 229.
    private const int ColumnPermissionDenied = 230;

    // Connection-level failures: timeout, network, login and database-open errors.
    private static readonly HashSet<int> UnreachableErrors = [-2, 2, 53, 64, 233, 4060, 10053, 10054, 10060, 10061, 11001, 18456];

    private readonly ITenantContext _tenant;
    private readonly PulseReportingSettingsResolver _resolver;
    private readonly ILogger<PulseGateway> _logger;
    private ResolvedPulseReporting? _resolved;

    public PulseGateway(ITenantContext tenant, PulseReportingSettingsResolver resolver, ILogger<PulseGateway> logger)
    {
        _tenant = tenant;
        _resolver = resolver;
        _logger = logger;
    }

    public Task<PulseResult> WriteHeartbeatAsync(string deviceId, DateTime heartbeatUtc, CancellationToken ct = default) =>
        UpdateDeviceAsync(
            "UPDATE dbo.tblDevice SET Heartbeat = @HeartbeatUtc WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId",
            deviceId,
            cmd => cmd.Parameters.Add("@HeartbeatUtc", SqlDbType.DateTime).Value = TruncateToSecond(heartbeatUtc),
            ct);

    /// <summary>
    /// Inserts one reading. On a <c>PK_tblPulse</c> collision it retries once a second earlier:
    /// the stamp is already truncated, so a later one could land in the future, and the
    /// previous cycle's row is a full cycle older so an earlier one cannot reorder it.
    /// </summary>
    public async Task<PulseResult> InsertReadingAsync(PulseReading reading, CancellationToken ct = default)
    {
        if (reading.ErrorLevel is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(reading), reading.ErrorLevel, "ErrorLevel must be 0 to 4.");
        GuardDeviceId(reading.DeviceId);

        var stamp = TruncateToSecond(reading.StampUtc);
        var result = await InsertReadingAtAsync(reading, stamp, ct);
        if (result.Outcome != PulseOutcome.KeyCollision)
            return result;

        _logger.LogInformation("Pulse reading for {DeviceId} item {ItemId} collided at {Stamp:O}; retrying a second earlier",
            reading.DeviceId, reading.MonitorItemId, stamp);
        return await InsertReadingAtAsync(reading, stamp.AddSeconds(-1), ct);
    }

    /// <summary>
    /// The customer's devices, each with the other customers that hold the same DeviceID. One query per
    /// call: the rows sharing a DeviceID come back with the customer's own and are folded here.
    /// </summary>
    public async Task<PulseResult<IReadOnlyList<PulseDevice>>> ReadDevicesAsync(CancellationToken ct = default)
    {
        var rows = await QueryAsync(
            """
            SELECT DeviceID, CustomerID, Description, Monitor, Snooze, SnoozeTime, Heartbeat
            FROM dbo.tblDevice
            WHERE DeviceID IN (SELECT DeviceID FROM dbo.tblDevice WHERE CustomerID = @CustomerId)
            """,
            addCustomer: true,
            null,
            r => (DeviceId: r.GetString(0),
                  CustomerId: r.GetString(1),
                  Description: r.IsDBNull(2) ? null : r.GetString(2),
                  Monitor: r.GetInt32(3),
                  Snooze: r.GetBoolean(4),
                  SnoozeTime: AsUtc(r, 5),
                  Heartbeat: AsUtc(r, 6)),
            ct);
        if (!rows.IsSuccess)
            return new(rows.Outcome, null, rows.Error);

        var customerId = Resolved().Settings.CustomerId;
        bool IsOwn(string c) => string.Equals(c, customerId, StringComparison.OrdinalIgnoreCase);
        var devices = rows.Value!
            .GroupBy(r => r.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Own: g.FirstOrDefault(r => IsOwn(r.CustomerId)), Others: g.Where(r => !IsOwn(r.CustomerId)).Select(r => r.CustomerId).Order().ToList()))
            .Where(x => x.Own.DeviceId is not null)
            .Select(x => new PulseDevice(x.Own.DeviceId, x.Own.Description, x.Own.Monitor, x.Own.Snooze, x.Own.SnoozeTime, x.Own.Heartbeat, x.Others))
            .OrderBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new(PulseOutcome.Success, devices);
    }

    // DeviceTypeID 0 is "All", Pulse's filter entry rather than a type a device can have.
    public Task<PulseResult<IReadOnlyList<PulseDeviceType>>> ReadDeviceTypesAsync(CancellationToken ct = default) =>
        QueryAsync<PulseDeviceType>(
            "SELECT DeviceTypeID, Description FROM dbo.tblDeviceType WHERE DeviceTypeID <> 0 ORDER BY Description",
            addCustomer: false,
            null,
            r => new PulseDeviceType(r.GetInt32(0), r.GetString(1)),
            ct);

    public Task<PulseResult<IReadOnlyList<PulseMonitorItem>>> ReadMonitorItemsAsync(CancellationToken ct = default) =>
        QueryAsync<PulseMonitorItem>(
            "SELECT MonitorItemID, Description, ItemTypeID FROM dbo.tblMonitoritem ORDER BY MonitorItemID",
            addCustomer: false,
            null,
            r => new PulseMonitorItem(
                r.GetInt32(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetInt32(2)),
            ct);

    /// <summary>Checks a DeviceID before Create in Pulse: refuse when the customer has it, warn when another does.</summary>
    public async Task<PulseResult<PulseDeviceIdUsage>> CheckDeviceIdAsync(string deviceId, CancellationToken ct = default)
    {
        GuardDeviceId(deviceId);
        var rows = await QueryAsync<string>(
            "SELECT CustomerID FROM dbo.tblDevice WHERE DeviceID = @DeviceId",
            addCustomer: false,
            cmd => cmd.Parameters.Add("@DeviceId", SqlDbType.VarChar, MaxDeviceIdLength).Value = deviceId,
            r => r.GetString(0),
            ct);
        if (!rows.IsSuccess)
            return new(rows.Outcome, null, rows.Error);

        var customerId = Resolved().Settings.CustomerId;
        var usage = new PulseDeviceIdUsage(
            rows.Value!.Contains(customerId, StringComparer.OrdinalIgnoreCase),
            rows.Value!.Where(c => !string.Equals(c, customerId, StringComparison.OrdinalIgnoreCase)).ToList());
        return new(PulseOutcome.Success, usage);
    }

    /// <summary>
    /// Every NOT NULL column is set explicitly: Pulse defaults <c>Monitor</c> to 1, and a monitored
    /// device with no heartbeat raises a Mission Critical alert within minutes.
    /// </summary>
    public async Task<PulseResult> CreateDeviceAsync(PulseNewDevice device, CancellationToken ct = default)
    {
        GuardDeviceId(device.DeviceId);
        if (device.Monitor && device.HeartbeatUtc is null)
            throw new ArgumentException("A monitored device must be created with a heartbeat.", nameof(device));

        var description = device.Description.Length > MaxDescriptionLength
            ? device.Description[..MaxDescriptionLength]
            : device.Description;

        return await ExecuteAsync(
            """
            INSERT INTO dbo.tblDevice
                (DeviceID, CustomerID, Description, DeviceTypeID, MonitorTemplateID,
                 Monitor, Debug, Snooze, IsMaster, Heartbeat)
            VALUES
                (@DeviceId, @CustomerId, @Description, @DeviceTypeId, 1,
                 @Monitor, 0, 0, 0, @HeartbeatUtc)
            """,
            cmd =>
            {
                cmd.Parameters.Add("@DeviceId", SqlDbType.VarChar, MaxDeviceIdLength).Value = device.DeviceId;
                cmd.Parameters.Add("@Description", SqlDbType.VarChar, MaxDescriptionLength).Value = description;
                cmd.Parameters.Add("@DeviceTypeId", SqlDbType.Int).Value = device.DeviceTypeId;
                cmd.Parameters.Add("@Monitor", SqlDbType.Int).Value = device.Monitor ? 1 : 0;
                cmd.Parameters.Add("@HeartbeatUtc", SqlDbType.DateTime).Value =
                    device.Monitor ? TruncateToSecond(device.HeartbeatUtc!.Value) : DBNull.Value;
            },
            expectRow: false,
            ct);
    }

    /// <summary>
    /// <c>SnoozeTime</c> is UTC because Pulse's unsnooze job compares it with GETUTCDATE().
    /// No tblDeviceHistory row is written, and an expired snooze is left for Pulse to clear.
    /// </summary>
    public Task<PulseResult> SnoozeAsync(string deviceId, DateTime untilUtc, CancellationToken ct = default) =>
        UpdateDeviceAsync(
            "UPDATE dbo.tblDevice SET Snooze = 1, SnoozeTime = @UntilUtc WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId",
            deviceId,
            cmd => cmd.Parameters.Add("@UntilUtc", SqlDbType.DateTime).Value = TruncateToSecond(untilUtc),
            ct);

    public Task<PulseResult> UnsnoozeAsync(string deviceId, CancellationToken ct = default) =>
        UpdateDeviceAsync(
            "UPDATE dbo.tblDevice SET Snooze = 0, SnoozeTime = NULL WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId",
            deviceId,
            _ => { },
            ct);

    /// <summary>
    /// The operator's Start or Stop monitoring. Starting writes the heartbeat in the same UPDATE, so Pulse
    /// never sees a monitored device with a stale heartbeat and raises no instant down alert.
    /// No tblDeviceHistory or tblAlert row is written, matching Snooze.
    /// </summary>
    public Task<PulseResult> SetMonitorAsync(string deviceId, bool monitor, DateTime? heartbeatUtc, CancellationToken ct = default)
    {
        if (!monitor)
            return UpdateDeviceAsync(
                "UPDATE dbo.tblDevice SET Monitor = 0 WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId",
                deviceId,
                _ => { },
                ct);

        if (heartbeatUtc is not { } heartbeat)
            throw new ArgumentException("Monitoring must start with a heartbeat.", nameof(heartbeatUtc));
        return UpdateDeviceAsync(
            "UPDATE dbo.tblDevice SET Monitor = 1, Heartbeat = @HeartbeatUtc WHERE DeviceID = @DeviceId AND CustomerID = @CustomerId",
            deviceId,
            cmd => cmd.Parameters.Add("@HeartbeatUtc", SqlDbType.DateTime).Value = TruncateToSecond(heartbeat),
            ct);
    }

    private Task<PulseResult> InsertReadingAtAsync(PulseReading reading, DateTime stampUtc, CancellationToken ct) =>
        ExecuteAsync(
            """
            INSERT INTO dbo.tblPulse
                (DeviceID, CustomerID, Heartbeat, MonitorItemID,
                 ValueVarchar, ValueInteger, ValueDateTime, ErrorLevel, LastEditUser)
            VALUES
                (@DeviceId, @CustomerId, @StampUtc, @MonitorItemId,
                 @Summary, @OpenIssueCount, @OldestTriggeredUtc, @ErrorLevel, @LastEditUser)
            """,
            cmd =>
            {
                cmd.Parameters.Add("@DeviceId", SqlDbType.VarChar, MaxDeviceIdLength).Value = reading.DeviceId;
                cmd.Parameters.Add("@StampUtc", SqlDbType.DateTime).Value = stampUtc;
                cmd.Parameters.Add("@MonitorItemId", SqlDbType.Int).Value = reading.MonitorItemId;
                cmd.Parameters.Add("@Summary", SqlDbType.VarChar, -1).Value = reading.Summary;
                cmd.Parameters.Add("@OpenIssueCount", SqlDbType.Int).Value = reading.OpenIssueCount;
                cmd.Parameters.Add("@OldestTriggeredUtc", SqlDbType.DateTime).Value =
                    reading.OldestTriggeredUtc is { } oldest ? TruncateToSecond(oldest) : DBNull.Value;
                cmd.Parameters.Add("@ErrorLevel", SqlDbType.VarChar, 50).Value = reading.ErrorLevel.ToString();
                cmd.Parameters.Add("@LastEditUser", SqlDbType.VarChar, 50).Value = WriterName;
            },
            expectRow: false,
            ct);

    private Task<PulseResult> UpdateDeviceAsync(string sql, string deviceId, Action<SqlCommand> bind, CancellationToken ct)
    {
        GuardDeviceId(deviceId);
        return ExecuteAsync(sql, cmd =>
        {
            cmd.Parameters.Add("@DeviceId", SqlDbType.VarChar, MaxDeviceIdLength).Value = deviceId;
            bind(cmd);
        }, expectRow: true, ct);
    }

    private async Task<PulseResult> ExecuteAsync(string sql, Action<SqlCommand> bind, bool expectRow, CancellationToken ct)
    {
        if (NotConfigured() is { } notConfigured)
            return notConfigured;

        try
        {
            await using var conn = new SqlConnection(Resolved().ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            AddCustomer(cmd);
            bind(cmd);
            var rows = await cmd.ExecuteNonQueryAsync(ct);

            // A zero-row UPDATE is the only sign an UPDATE gives that the linked device is gone.
            return expectRow && rows == 0
                ? new PulseResult(PulseOutcome.DeviceMissing, "No tblDevice row matched the DeviceID under this customer.")
                : PulseResult.Ok;
        }
        catch (SqlException ex)
        {
            return Classify(ex);
        }
    }

    private async Task<PulseResult<IReadOnlyList<T>>> QueryAsync<T>(
        string sql, bool addCustomer, Action<SqlCommand>? bind, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        if (NotConfigured() is { } notConfigured)
            return new(notConfigured.Outcome, null, notConfigured.Error);

        try
        {
            await using var conn = new SqlConnection(Resolved().ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            if (addCustomer)
                AddCustomer(cmd);
            bind?.Invoke(cmd);

            var rows = new List<T>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(map(reader));
            return new(PulseOutcome.Success, rows);
        }
        catch (SqlException ex)
        {
            var result = Classify(ex);
            return new(result.Outcome, null, result.Error);
        }
    }

    private PulseResult Classify(SqlException ex)
    {
        var outcome = ex.Number switch
        {
            ForeignKeyViolation when ex.Message.Contains("FK_tblPulse_tblDevice", StringComparison.OrdinalIgnoreCase) => PulseOutcome.DeviceMissing,
            ForeignKeyViolation when ex.Message.Contains("FK_tblPulse_tblMonitoritem", StringComparison.OrdinalIgnoreCase) => PulseOutcome.MonitorItemMissing,
            PrimaryKeyViolation when ex.Message.Contains("PK_tblPulse", StringComparison.OrdinalIgnoreCase) => PulseOutcome.KeyCollision,
            PrimaryKeyViolation when ex.Message.Contains("PK_tblDevice", StringComparison.OrdinalIgnoreCase) => PulseOutcome.DuplicateDeviceId,
            ObjectPermissionDenied or ColumnPermissionDenied => PulseOutcome.PermissionDenied,
            _ when UnreachableErrors.Contains(ex.Number) => PulseOutcome.Unreachable,
            _ => PulseOutcome.Rejected,
        };

        if (outcome is PulseOutcome.Unreachable or PulseOutcome.Rejected or PulseOutcome.PermissionDenied)
            _logger.LogWarning(ex, "Pulse call for tenant {Tenant} failed with SQL error {Number}", Resolved().Subdomain, ex.Number);

        return new PulseResult(outcome, $"SQL {ex.Number}: {ex.Message}");
    }

    private PulseResult? NotConfigured()
    {
        ResolvedPulseReporting resolved;
        try
        {
            resolved = Resolved();
        }
        catch (InvalidOperationException ex)
        {
            return new(PulseOutcome.NotConfigured, ex.Message);
        }

        if (string.IsNullOrWhiteSpace(resolved.ConnectionString))
            return new(PulseOutcome.NotConfigured, $"No Pulse connection string resolves for tenant '{resolved.Subdomain}'.");
        if (string.IsNullOrWhiteSpace(resolved.Settings.CustomerId))
            return new(PulseOutcome.NotConfigured, $"Pulse reporting has no CustomerId for tenant '{resolved.Subdomain}'.");
        return null;
    }

    // A NULL CustomerID hides a reading from every Pulse view, so the value always comes from
    // the resolved settings and NotConfigured() has already refused a blank one.
    private void AddCustomer(SqlCommand cmd) =>
        cmd.Parameters.Add("@CustomerId", SqlDbType.VarChar, 20).Value = Resolved().Settings.CustomerId;

    private ResolvedPulseReporting Resolved()
    {
        if (_resolved is not null)
            return _resolved;
        if (!_tenant.IsPopulated)
            throw new InvalidOperationException("PulseGateway needs a populated tenant scope.");
        return _resolved = _resolver.Resolve(_tenant.Subdomain);
    }

    private static void GuardDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > MaxDeviceIdLength)
            throw new ArgumentException($"A Pulse DeviceID must be 1 to {MaxDeviceIdLength} characters.", nameof(deviceId));
    }

    private static DateTime TruncateToSecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private static DateTime? AsUtc(SqlDataReader r, int ordinal) =>
        r.IsDBNull(ordinal) ? null : DateTime.SpecifyKind(r.GetDateTime(ordinal), DateTimeKind.Utc);
}
