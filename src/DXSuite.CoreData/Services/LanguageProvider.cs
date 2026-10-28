using DXSuite.CoreData.Entities;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.CoreData.Services;

public class LanguageProvider : ILanguageProvider
{
    private readonly IDbContextFactory<CoreDataDbContext> _dbFactory;

    public LanguageProvider(IDbContextFactory<CoreDataDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.TblLanguages
            .AsNoTracking()
            .Select(l => new LanguageInfo(0, l.LanguageCode, l.LanguageName, true))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<CountryLanguageInfo>> GetCountryLanguagesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.TblCountryLanguages
            .AsNoTracking()
            .Select(cl => new CountryLanguageInfo(0, cl.CountryCode, cl.LanguageCode, cl.CountryCode))
            .ToListAsync();
    }
}
