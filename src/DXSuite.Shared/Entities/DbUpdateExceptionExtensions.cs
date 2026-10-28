using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities;

public static class DbUpdateExceptionExtensions
{
    /// <summary>
    /// True when the save failed because a unique index (2601) or unique constraint (2627)
    /// rejected the row — the signal that a concurrent writer won an insert race.
    /// </summary>
    public static bool IsUniqueViolation(this DbUpdateException ex)
        => ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
}
