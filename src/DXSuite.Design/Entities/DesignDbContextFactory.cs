using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DXSuite.Design.Entities;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to construct <see cref="DesignDbContext"/>
/// without running the Host application's startup pipeline. The connection string below is a
/// placeholder — EF never opens this connection when generating migrations, scripts, or comparing
/// the model. At runtime the context is constructed by DI with the tenant's real Design connection.
/// </summary>
public sealed class DesignDbContextFactory : IDesignTimeDbContextFactory<DesignDbContext>
{
    public DesignDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DesignDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=DXSuite_Design_DesignTime;Trusted_Connection=true")
            .Options;

        return new DesignDbContext(options);
    }
}
