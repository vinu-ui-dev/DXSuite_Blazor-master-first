using DXSuite.Aviation.Entities;
using DXSuite.Shared.Logging;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DXSuite.Aviation.Services;

/// <summary>
/// An airline's logo for one role: the file on screen now, and a newer file still going through the library's
/// approval chain (Director &gt; Approvals), if any.
/// </summary>
/// <param name="Live">The approved file counters draw. Null until a first file is approved.</param>
/// <param name="Pending">A file waiting on approval, or one that was declined. Counters keep drawing <paramref name="Live"/>.</param>
/// <param name="PendingDeclined">True when <paramref name="Pending"/> was declined (or deleted) rather than still waiting.</param>
public sealed record AirlineLogoState(AirlineLogoFile? Live, AirlineLogoFile? Pending, bool PendingDeclined);

/// <summary>
/// Links an airline's logo roles (wordmark, square, reversed) to files of the Director airline logo library. Files are
/// uploaded to the library, which sends them through its approval chain; this keeps only the pointers.
/// </summary>
/// <remarks>
/// Two rows can exist per role in <c>tblAirlineLogo</c>: the role itself (the approved logo on screen) and
/// <c>{role}-pending</c> (a newer upload not approved yet). The role row only ever points at an approved file, so a
/// new upload never replaces the logo on screen until Director approves it; <see cref="LoadAsync"/> promotes it then.
/// </remarks>
public sealed class AirlineLogoLinks(
    IDbContextFactory<AviationDbContext> dbFactory,
    IServiceProvider services,
    ILogger<AirlineLogoLinks> logger)
{
    public static readonly string[] Roles = ["wordmark", "square", "reversed"];

    private const string PendingSuffix = "-pending";
    private const int NameLength = 255;

    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaReady;

    // The same script as db/migrations/aviation-airline-logo.sql, so the links save on a database nobody ran it on.
    private const string SchemaScript = """
        IF OBJECT_ID(N'dbo.tblAirlineLogo', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.tblAirlineLogo (
                AirlineLogoId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblAirlineLogo PRIMARY KEY,
                AirlineCode   VARCHAR(3)    NOT NULL,
                [Role]        VARCHAR(20)   NOT NULL,
                AssetId       INT           NOT NULL,
                AssetName     NVARCHAR(255) NULL,
                AssetPath     VARCHAR(255)  NOT NULL,
                LinkedUtc     DATETIME2     NOT NULL
            );
            CREATE UNIQUE INDEX IX_tblAirlineLogo_Airline_Role ON dbo.tblAirlineLogo (AirlineCode, [Role]);
        END
        """;

    private async Task EnsureSchemaAsync(AviationDbContext db, CancellationToken ct)
    {
        if (_schemaReady) return;
        await SchemaGate.WaitAsync(ct);
        try
        {
            if (_schemaReady) return;
            await db.Database.ExecuteSqlRawAsync(SchemaScript, ct);
            _schemaReady = true;
        }
        catch (Exception ex)
        {
            AirlineFileLog.Write($"[LOGO-LINK] tblAirlineLogo is missing and could not be created: {AirlineFileLog.Describe(ex)}");
            throw;
        }
        finally
        {
            SchemaGate.Release();
        }
    }

    /// <summary>Links <paramref name="file"/> to the airline's <paramref name="role"/>: live when approved, otherwise as the pending upload.</summary>
    public async Task LinkAsync(string code, string role, AirlineLogoFile file, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSchemaAsync(db, ct);
        AirlineFileLog.Write($"[LOGO-LINK] {code} {role} -> asset {file.AssetId} '{file.Name}' path='{file.Path}' approved={file.Approved}");
        var rows = await db.TblAirlineLogos
            .Where(l => l.AirlineCode == code && (l.Role == role || l.Role == role + PendingSuffix))
            .ToListAsync(ct);

        if (file.Approved)
        {
            Upsert(db, rows, code, role, file);
            foreach (var pending in rows.Where(r => r.Role == role + PendingSuffix)) db.TblAirlineLogos.Remove(pending);
        }
        else
        {
            Upsert(db, rows, code, role + PendingSuffix, file);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The airline's logos by role. A pending upload that has been approved since is promoted to the logo on screen
    /// here, so opening the airline after Director approves it shows it live.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, AirlineLogoState>> LoadAsync(string code, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSchemaAsync(db, ct);
        var rows = await db.TblAirlineLogos.Where(l => l.AirlineCode == code).ToListAsync(ct);
        if (rows.Count == 0) return new Dictionary<string, AirlineLogoState>();

        var states = await ApprovalStatesAsync(rows.Where(r => r.Role.EndsWith(PendingSuffix)).Select(r => r.AssetId).ToList(), ct);

        var result = new Dictionary<string, AirlineLogoState>();
        foreach (var role in Roles)
        {
            var live = rows.FirstOrDefault(r => r.Role == role);
            var pending = rows.FirstOrDefault(r => r.Role == role + PendingSuffix);
            var declined = false;

            if (pending is not null)
            {
                var state = states.TryGetValue(pending.AssetId, out var s) ? s : LogoApprovalState.Pending;
                if (state == LogoApprovalState.Approved)
                {
                    if (live is null)
                    {
                        // The pending row becomes the live one.
                        pending.Role = role;
                        live = pending;
                    }
                    else
                    {
                        live.AssetId = pending.AssetId;
                        live.AssetName = pending.AssetName;
                        live.AssetPath = pending.AssetPath;
                        live.LinkedUtc = DateTime.UtcNow;
                        db.TblAirlineLogos.Remove(pending);
                    }
                    pending = null;
                }
                else
                {
                    declined = state is LogoApprovalState.Declined or LogoApprovalState.Gone;
                }
            }

            if (live is null && pending is null) continue;
            result[role] = new AirlineLogoState(ToFile(live, approved: true), ToFile(pending, approved: false), declined);
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<IReadOnlyDictionary<int, LogoApprovalState>> ApprovalStatesAsync(IReadOnlyCollection<int> assetIds, CancellationToken ct)
    {
        if (assetIds.Count == 0 || services.GetService(typeof(IAirlineLogoLibrary)) is not IAirlineLogoLibrary library)
            return new Dictionary<int, LogoApprovalState>();

        try { return await library.GetApprovalStatesAsync(assetIds, ct); }
        catch (Exception ex)
        {
            // Unknown is treated as still pending: nothing is promoted or discarded on a failed read.
            logger.LogWarning(ex, "Could not read the approval state of the airline logo files");
            return new Dictionary<int, LogoApprovalState>();
        }
    }

    private static void Upsert(AviationDbContext db, List<TblAirlineLogo> rows, string code, string role, AirlineLogoFile file)
    {
        var row = rows.FirstOrDefault(r => r.Role == role);
        if (row is null)
        {
            row = new TblAirlineLogo { AirlineCode = code, Role = role };
            db.TblAirlineLogos.Add(row);
        }
        row.AssetId = file.AssetId;
        row.AssetName = file.Name.Length > NameLength ? file.Name[..NameLength] : file.Name;
        row.AssetPath = file.Path;
        row.LinkedUtc = DateTime.UtcNow;
    }

    private static AirlineLogoFile? ToFile(TblAirlineLogo? row, bool approved) =>
        row is null ? null : new AirlineLogoFile(row.AssetId, row.AssetName ?? row.AssetPath, row.AssetPath, approved);
}
