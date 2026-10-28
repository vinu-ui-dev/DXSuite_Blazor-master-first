using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DXSuite.Shared.Entities.Registry;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling to construct
/// <see cref="TenantRegistryDbContext"/> without running the Host application's startup pipeline.
/// The connection string below is a placeholder — EF never opens it when generating migrations,
/// scripts, or comparing the model. At runtime the context is constructed by DI with the real
/// <c>ConnectionStrings:TenantRegistry</c> value.
/// </summary>
public sealed class TenantRegistryDbContextFactory : IDesignTimeDbContextFactory<TenantRegistryDbContext>
{
    public TenantRegistryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantRegistryDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=DXSuite_TenantRegistry_DesignTime;Trusted_Connection=true")
            .Options;

        return new TenantRegistryDbContext(options);
    }
}
