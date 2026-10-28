using DXSuite.Design.Entities;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Design.Services;

public class NetsuiteConnectionProvider : INetsuiteConnectionProvider
{
    private readonly IDbContextFactory<DesignDbContext> _dbFactory;
    private readonly ITenantContext _tenant;

    public NetsuiteConnectionProvider(IDbContextFactory<DesignDbContext> dbFactory, ITenantContext tenant)
    {
        _dbFactory = dbFactory;
        _tenant = tenant;
    }

    public async Task<TblDesignNetsuiteConnection?> GetActiveConnectionAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TblDesignNetsuiteConnections
            .AsNoTracking()
            .Where(c => c.CustomerId == _tenant.CustomerCode && c.IsActive)
            .FirstOrDefaultAsync(ct);
    }
}
