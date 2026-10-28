using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Services;

public class UserPreferenceService : IDirectorUserPreferenceService
{
    private readonly IDirectorDBContext _dbContext;

    public UserPreferenceService(IDirectorDBContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ConfigurationViewSettings?> GetConfigurationViewSettingsAsync(int userId)
    {
        var user = await _dbContext.TblUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null || string.IsNullOrEmpty(user.ExtendedData))
            return null;

        var extData = ExtendedDataHelper.Parse<UserExtendedData>(user.ExtendedData);
        return extData?.ConfigurationView;
    }

    public async Task SaveConfigurationViewSettingsAsync(int userId, ConfigurationViewSettings settings)
    {
        var user = await _dbContext.TblUsers.FindAsync(userId);
        if (user == null)
            return;

        user.ExtendedData = ExtendedDataHelper.UpdateProperty<UserExtendedData>(
            user.ExtendedData,
            data => data.ConfigurationView = settings
        );

        await _dbContext.SaveChangesAsync();
    }

    public async Task ClearConfigurationViewSettingsAsync(int userId)
    {
        var user = await _dbContext.TblUsers.FindAsync(userId);
        if (user == null)
            return;

        user.ExtendedData = ExtendedDataHelper.UpdateProperty<UserExtendedData>(
            user.ExtendedData,
            data => data.ConfigurationView = null
        );

        await _dbContext.SaveChangesAsync();
    }

    public async Task<string?> GetPreferredLanguageAsync(int userId)
    {
        var user = await _dbContext.TblUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null || string.IsNullOrEmpty(user.ExtendedData))
            return null;

        var extData = ExtendedDataHelper.Parse<UserExtendedData>(user.ExtendedData);
        return extData?.PreferredLanguage;
    }

    public async Task SavePreferredLanguageAsync(int userId, string languageCode)
    {
        var user = await _dbContext.TblUsers.FindAsync(userId);
        if (user == null)
            return;

        user.ExtendedData = ExtendedDataHelper.UpdateProperty<UserExtendedData>(
            user.ExtendedData,
            data => data.PreferredLanguage = languageCode
        );

        await _dbContext.SaveChangesAsync();
    }

    public async Task<string?> GetDefaultLandingPageAsync(int userId)
    {
        var user = await _dbContext.TblUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null || string.IsNullOrEmpty(user.ExtendedData))
            return null;

        var extData = ExtendedDataHelper.Parse<UserExtendedData>(user.ExtendedData);
        return extData?.DefaultLandingPage;
    }

    public async Task SaveDefaultLandingPageAsync(int userId, string? route)
    {
        var user = await _dbContext.TblUsers.FindAsync(userId);
        if (user == null)
            return;

        user.ExtendedData = ExtendedDataHelper.UpdateProperty<UserExtendedData>(
            user.ExtendedData,
            data => data.DefaultLandingPage = route
        );

        await _dbContext.SaveChangesAsync();
    }

    public async Task<bool> GetKioskModeAsync(int userId)
    {
        var user = await _dbContext.TblUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null || string.IsNullOrEmpty(user.ExtendedData))
            return false;

        return ExtendedDataHelper.Parse<UserExtendedData>(user.ExtendedData)?.KioskMode == true;
    }

    public async Task SaveKioskModeAsync(int userId, bool kioskMode)
    {
        var user = await _dbContext.TblUsers.FindAsync(userId);
        if (user == null)
            return;

        user.ExtendedData = ExtendedDataHelper.UpdateProperty<UserExtendedData>(
            user.ExtendedData,
            // Null rather than false when off, so the property stays absent from the stored JSON
            // for the overwhelming majority of accounts that are not kiosks.
            data => data.KioskMode = kioskMode ? true : null
        );

        await _dbContext.SaveChangesAsync();
    }
}
