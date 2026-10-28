using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DXSuite.Director.Entities;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to construct <see cref="DirectorDbContext"/>
/// without running the Host application's startup pipeline. The connection string below is a
/// placeholder — EF never opens it when generating migrations, scripts, or comparing the model.
/// At runtime the context is constructed by DI with the tenant's real Director connection.
/// </summary>
public sealed class DirectorDbContextFactory : IDesignTimeDbContextFactory<DirectorDbContext>
{
    public DirectorDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DirectorDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=DXSuite_Director_DesignTime;Trusted_Connection=true")
            .Options;

        return new DirectorDbContext(options);
    }
}
