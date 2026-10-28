using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to construct <see cref="PlatformDbContext"/>
/// without running the Host application's startup pipeline. The connection string below is a
/// placeholder — EF never actually opens this connection when generating migrations or scripts.
/// At runtime the context is constructed by DI with the real <c>ConnectionStrings:Platform</c> value.
/// </summary>
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=DXSuite_Platform_Design;Trusted_Connection=true")
            .Options;

        return new PlatformDbContext(options);
    }
}
