using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to construct <see cref="SentinelDBContext"/>
/// without running the Host application's startup pipeline. The connection string below is a
/// placeholder — EF never opens it when generating migrations, scripts, or comparing the model.
/// At runtime the context is constructed by DI with the tenant's real Sentinel connection.
/// </summary>
public sealed class SentinelDBContextFactory : IDesignTimeDbContextFactory<SentinelDBContext>
{
    public SentinelDBContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SentinelDBContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=DXSuite_Sentinel_DesignTime;Trusted_Connection=true")
            .Options;

        return new SentinelDBContext(options);
    }
}
