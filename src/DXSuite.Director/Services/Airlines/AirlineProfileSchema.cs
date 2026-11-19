using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Airlines;

/// <summary>
/// Makes sure aviation.tblAirlineProfile exists (the same script as db/migrations/director-airline-profile.sql), so the
/// CUPPS options save on a database nobody ran the script on. It runs once per process and only creates the table
/// when it is missing.
/// </summary>
public sealed class AirlineProfileSchema(DirectorDbContext db, ILogger<AirlineProfileSchema> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _ready;

    private const string Script = """
        IF OBJECT_ID(N'aviation.tblAirlineProfile', N'U') IS NULL
        BEGIN
            CREATE TABLE aviation.tblAirlineProfile (
                AirlineId       INT NOT NULL CONSTRAINT PK_tblAirlineProfile PRIMARY KEY,
                SunflowerOption BIT NOT NULL CONSTRAINT DF_tblAirlineProfile_Sunflower DEFAULT (0),
                AslVideos       BIT NOT NULL CONSTRAINT DF_tblAirlineProfile_Asl DEFAULT (1),
                DestinationZone BIT NOT NULL CONSTRAINT DF_tblAirlineProfile_Dest DEFAULT (1)
            );
        END
        """;

    /// <summary>Throws when the table is missing and cannot be created (for example, no permission to create tables).</summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (_ready) return;
        await Gate.WaitAsync(ct);
        try
        {
            if (_ready) return;
            await db.Database.ExecuteSqlRawAsync(Script, ct);
            _ready = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "aviation.tblAirlineProfile is missing and could not be created. Run db/migrations/director-airline-profile.sql.");
            throw;
        }
        finally
        {
            Gate.Release();
        }
    }
}
