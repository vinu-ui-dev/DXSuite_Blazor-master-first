using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DXSuite.Director.Migrations
{
    /// <inheritdoc />
    public partial class UniqueUserPrincipalName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Duplicate tblUser rows can exist from the pre-index find-or-create race in
            // SessionState (concurrent first-login circuits). Collapse each duplicate set onto
            // its lowest UserId before creating the unique index: repoint every FK that
            // references tblUser(UserId), then delete the extra rows. FK discovery is dynamic so
            // the cleanup covers all referencing tables regardless of deployment. If a repoint
            // would violate a unique constraint on a referencing table the migration fails and
            // that data must be reconciled manually.
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM [tblUser]
    WHERE [UserPrincipalName] IS NOT NULL
    GROUP BY [UserPrincipalName]
    HAVING COUNT(*) > 1)
BEGIN
    CREATE TABLE #DuplicateUsers (DupUserId int PRIMARY KEY, KeepUserId int NOT NULL);

    INSERT INTO #DuplicateUsers (DupUserId, KeepUserId)
    SELECT u.[UserId], k.KeepUserId
    FROM [tblUser] u
    INNER JOIN (
        SELECT [UserPrincipalName], MIN([UserId]) AS KeepUserId
        FROM [tblUser]
        WHERE [UserPrincipalName] IS NOT NULL
        GROUP BY [UserPrincipalName]
        HAVING COUNT(*) > 1
    ) k ON u.[UserPrincipalName] = k.[UserPrincipalName]
    WHERE u.[UserId] <> k.KeepUserId;

    DECLARE @sql nvarchar(max) = N'';
    SELECT @sql += N'UPDATE t SET t.' + QUOTENAME(pc.name) + N' = d.KeepUserId FROM '
                 + QUOTENAME(sch.name) + N'.' + QUOTENAME(pt.name) + N' t'
                 + N' INNER JOIN #DuplicateUsers d ON t.' + QUOTENAME(pc.name) + N' = d.DupUserId;' + CHAR(10)
    FROM sys.foreign_keys fk
    INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
    INNER JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
    INNER JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
    INNER JOIN sys.schemas sch ON sch.schema_id = pt.schema_id
    INNER JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
    WHERE rt.name = 'tblUser' AND rc.name = 'UserId';

    EXEC sp_executesql @sql;

    DELETE u FROM [tblUser] u INNER JOIN #DuplicateUsers d ON u.[UserId] = d.DupUserId;

    DROP TABLE #DuplicateUsers;
END
");

            migrationBuilder.CreateIndex(
                name: "UX_tblUser_UserPrincipalName",
                table: "tblUser",
                column: "UserPrincipalName",
                unique: true,
                filter: "[UserPrincipalName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_tblUser_UserPrincipalName",
                table: "tblUser");
        }
    }
}
