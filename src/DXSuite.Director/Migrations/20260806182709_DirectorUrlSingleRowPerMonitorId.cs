using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class DirectorUrlSingleRowPerMonitorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_tblDirectorUrl_MonitorUrl",
                table: "tblDirectorUrl");

            // Collapse per-endpoint rows into one row per monitor id before the
            // unique index lands: keep the page-URL row (the operator-entered URL)
            // or the most recent, merge seen-ranges, and preserve any monitoring
            // config that was set on a sibling row.
            migrationBuilder.Sql(@"
UPDATE k SET
    FirstSeenUtc            = agg.MinFirst,
    LastSeenUtc             = agg.MaxLast,
    IsMonitored             = CAST(agg.AnyMonitored AS bit),
    LinkedDeviceId          = COALESCE(k.LinkedDeviceId, agg.AnyDevice),
    ExpectedIntervalSeconds = COALESCE(agg.MonitoredInterval, k.ExpectedIntervalSeconds)
FROM tblDirectorUrl k
JOIN (
    SELECT DirectorUrlId,
           ROW_NUMBER() OVER (PARTITION BY MonitorId
               ORDER BY CASE WHEN UrlKey NOT LIKE '/api/%' THEN 0 ELSE 1 END, LastSeenUtc DESC) AS rn
    FROM tblDirectorUrl
) r ON r.DirectorUrlId = k.DirectorUrlId AND r.rn = 1
JOIN (
    SELECT MonitorId,
           MIN(FirstSeenUtc) AS MinFirst,
           MAX(LastSeenUtc)  AS MaxLast,
           MAX(CAST(IsMonitored AS int)) AS AnyMonitored,
           MAX(LinkedDeviceId) AS AnyDevice,
           MAX(CASE WHEN IsMonitored = 1 THEN ExpectedIntervalSeconds END) AS MonitoredInterval
    FROM tblDirectorUrl GROUP BY MonitorId
) agg ON agg.MonitorId = k.MonitorId;

DELETE FROM tblDirectorUrl WHERE DirectorUrlId NOT IN (
    SELECT DirectorUrlId FROM (
        SELECT DirectorUrlId,
               ROW_NUMBER() OVER (PARTITION BY MonitorId
                   ORDER BY CASE WHEN UrlKey NOT LIKE '/api/%' THEN 0 ELSE 1 END, LastSeenUtc DESC) AS rn
        FROM tblDirectorUrl
    ) x WHERE rn = 1);
");

            migrationBuilder.CreateIndex(
                name: "UX_tblDirectorUrl_MonitorId",
                table: "tblDirectorUrl",
                column: "MonitorId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_tblDirectorUrl_MonitorId",
                table: "tblDirectorUrl");

            migrationBuilder.CreateIndex(
                name: "UX_tblDirectorUrl_MonitorUrl",
                table: "tblDirectorUrl",
                columns: new[] { "MonitorId", "UrlKey" },
                unique: true);
        }
    }
}
