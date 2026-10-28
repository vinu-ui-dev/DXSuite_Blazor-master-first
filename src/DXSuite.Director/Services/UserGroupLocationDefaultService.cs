using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

public class UserGroupLocationDefaultService : IUserGroupLocationDefaultService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public UserGroupLocationDefaultService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<AirlineWallDefaults> GetDefaultForGroupAsync(int userGroupId, int locationId, DateTime? atTime = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var states = await db.TblUserGroupLocationDefaults
            .AsNoTracking()
            .Include(d => d.Logos)
            .Where(d => d.UserGroupId == userGroupId && d.LocationId == locationId)
            .ToListAsync();

        var record = AirlineStateResolver.PickActive(states, atTime ?? DateTime.Now);

        if (record != null)
            return new AirlineWallDefaults(record.DefaultConstraintId,
                    record.BackgroundPath, record.BackgroundMode,
                    record.MaskPath, record.MaskMode, record.LogoPath,
                    record.Logos.ToDictionary(l => l.LogoRoleKey,
                        l => new AirlineLogoContent(l.ContentPath, l.ContentMode)),
                    record.BackgroundOpacityPct, record.MaskOpacityPct);

        // Fall back to the location's GlobalConstraintId
        var location = await db.TblLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == locationId);

        if (location?.ExtendedData != null)
        {
            try
            {
                var ext = JsonSerializer.Deserialize<LocationExtendedData>(location.ExtendedData,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (ext?.GlobalConstraintId != null)
                    return new AirlineWallDefaults(ext.GlobalConstraintId, null, "fill-width", null, "fill-width", null,
                        AirlineWallDefaults.NoLogos);
            }
            catch { }
        }

        return new AirlineWallDefaults(null, null, "fill-width", null, "fill-width", null,
            AirlineWallDefaults.NoLogos);
    }

    public async Task<List<TblUserGroupLocationDefault>> GetDefaultsForGroupAsync(int userGroupId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblUserGroupLocationDefaults
            .AsNoTracking()
            .Include(d => d.Location)
            .Include(d => d.DefaultConstraint)
            .Where(d => d.UserGroupId == userGroupId)
            .OrderBy(d => d.Location.LocationName)
            .ToListAsync();
    }

    public async Task UpsertAsync(int userGroupId, int locationId, int? constraintId,
                                   string? backgroundPath, string backgroundMode,
                                   string? maskPath, string maskMode,
                                   string? logoPath = null,
                                   IReadOnlyDictionary<string, AirlineLogoContent>? logos = null,
                                   IReadOnlyList<AirlineLayerSegment>? backgroundSegments = null,
                                   IReadOnlyList<AirlineLayerSegment>? maskSegments = null,
                                   int backgroundOpacityPct = 100,
                                   int maskOpacityPct = 100,
                                   string stateName = "Default",
                                   string? stateScheduleJson = null,
                                   bool makeDefaultState = false)
    {
        backgroundOpacityPct = Math.Clamp(backgroundOpacityPct, 0, 100);
        maskOpacityPct       = Math.Clamp(maskOpacityPct, 0, 100);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var siblings = await db.TblUserGroupLocationDefaults
            .Include(d => d.Logos)
            .Include(d => d.LayerContents)
            .Where(d => d.UserGroupId == userGroupId && d.LocationId == locationId)
            .ToListAsync();

        var existing = siblings.FirstOrDefault(d => d.StateName == stateName);

        if (existing != null)
        {
            existing.DefaultConstraintId = constraintId;
            existing.BackgroundPath = backgroundPath;
            existing.BackgroundMode = backgroundMode;
            existing.BackgroundOpacityPct = backgroundOpacityPct;
            existing.MaskPath = maskPath;
            existing.MaskMode = maskMode;
            existing.MaskOpacityPct = maskOpacityPct;
            existing.LogoPath = logoPath;
            existing.StateScheduleJson = stateScheduleJson;
            existing.LastEditDate = DateTime.UtcNow;
        }
        else
        {
            existing = new TblUserGroupLocationDefault
            {
                UserGroupId = userGroupId,
                LocationId = locationId,
                StateName = stateName,
                // The first state for a pair is always the fallback
                IsDefaultState = siblings.Count == 0,
                StateScheduleJson = stateScheduleJson,
                DefaultConstraintId = constraintId,
                BackgroundPath = backgroundPath,
                BackgroundMode = backgroundMode,
                BackgroundOpacityPct = backgroundOpacityPct,
                MaskPath = maskPath,
                MaskMode = maskMode,
                MaskOpacityPct = maskOpacityPct,
                LogoPath = logoPath,
                CreateDate = DateTime.UtcNow,
                LastEditDate = DateTime.UtcNow
            };
            db.TblUserGroupLocationDefaults.Add(existing);
        }

        if (makeDefaultState && !existing.IsDefaultState)
        {
            foreach (var sibling in siblings.Where(s => s.IsDefaultState))
                sibling.IsDefaultState = false;
            existing.IsDefaultState = true;
        }

        // The fallback state plays whenever nothing matches; a schedule on it would be misleading
        if (existing.IsDefaultState)
            existing.StateScheduleJson = null;

        if (logos != null)
        {
            // The dictionary is the complete desired per-role set: empty-content
            // entries and rows for roles no longer present are removed.
            var desired = logos
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.ContentPath))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            foreach (var row in existing.Logos.Where(r => !desired.ContainsKey(r.LogoRoleKey)).ToList())
                existing.Logos.Remove(row);

            foreach (var (roleKey, content) in desired)
            {
                var row = existing.Logos.FirstOrDefault(r => r.LogoRoleKey == roleKey);
                if (row == null)
                {
                    existing.Logos.Add(new TblUserGroupLocationLogo
                    {
                        LogoRoleKey = roleKey,
                        ContentPath = content.ContentPath,
                        ContentMode = content.ContentMode,
                        CreateDate = DateTime.UtcNow,
                        LastEditDate = DateTime.UtcNow,
                    });
                }
                else if (row.ContentPath != content.ContentPath || row.ContentMode != content.ContentMode)
                {
                    row.ContentPath = content.ContentPath;
                    row.ContentMode = content.ContentMode;
                    row.LastEditDate = DateTime.UtcNow;
                }
            }
        }

        ReplaceLayerSegments(existing, "background", backgroundSegments);
        ReplaceLayerSegments(existing, "mask", maskSegments);

        await db.SaveChangesAsync();
    }

    // Ordering is positional, so a provided list fully replaces the role's rows
    // (SortOrder = list index) rather than diffing against existing ones.
    private static void ReplaceLayerSegments(TblUserGroupLocationDefault target,
        string layerRole, IReadOnlyList<AirlineLayerSegment>? segments)
    {
        if (segments == null) return;

        foreach (var row in target.LayerContents
                     .Where(r => string.Equals(r.LayerRole, layerRole, StringComparison.OrdinalIgnoreCase))
                     .ToList())
            target.LayerContents.Remove(row);

        int order = 0;
        foreach (var seg in segments.Where(s => !string.IsNullOrWhiteSpace(s.ContentPath)))
        {
            target.LayerContents.Add(new TblUserGroupLocationLayerContent
            {
                LayerRole = layerRole,
                SortOrder = order++,
                ContentPath = seg.ContentPath,
                IntrinsicWidthPx = Math.Max(0, seg.IntrinsicWidthPx),
                CreateDate = DateTime.UtcNow,
                LastEditDate = DateTime.UtcNow,
            });
        }
    }

    public async Task<List<TblUserGroupLocationDefault>> GetStatesAsync(int userGroupId, int locationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblUserGroupLocationDefaults
            .AsNoTracking()
            .Include(d => d.Logos)
            .Include(d => d.LayerContents)
            .Where(d => d.UserGroupId == userGroupId && d.LocationId == locationId)
            .OrderByDescending(d => d.IsDefaultState)
            .ThenBy(d => d.StateName)
            .ToListAsync();
    }

    public async Task<int> AddStateAsync(int userGroupId, int locationId, string stateName, int? copyFromRowId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        stateName = stateName.Trim();
        if (stateName.Length == 0) throw new ArgumentException("State name is required.");

        var siblings = await db.TblUserGroupLocationDefaults
            .Where(d => d.UserGroupId == userGroupId && d.LocationId == locationId)
            .ToListAsync();
        if (siblings.Any(s => string.Equals(s.StateName, stateName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A state named '{stateName}' already exists.");

        var source = copyFromRowId != null
            ? await db.TblUserGroupLocationDefaults
                .AsNoTracking()
                .Include(d => d.Logos)
                .Include(d => d.LayerContents)
                .FirstOrDefaultAsync(d => d.UserGroupLocationDefaultId == copyFromRowId)
            : null;

        var row = new TblUserGroupLocationDefault
        {
            UserGroupId = userGroupId,
            LocationId = locationId,
            StateName = stateName,
            IsDefaultState = siblings.Count == 0,
            DefaultConstraintId = source?.DefaultConstraintId,
            BackgroundPath = source?.BackgroundPath,
            BackgroundMode = source?.BackgroundMode ?? "fill-width",
            BackgroundOpacityPct = source?.BackgroundOpacityPct ?? 100,
            MaskPath = source?.MaskPath,
            MaskMode = source?.MaskMode ?? "fill-width",
            MaskOpacityPct = source?.MaskOpacityPct ?? 100,
            LogoPath = source?.LogoPath,
            CreateDate = DateTime.UtcNow,
            LastEditDate = DateTime.UtcNow
        };
        if (source != null)
            CopyChildren(source, row);

        db.TblUserGroupLocationDefaults.Add(row);
        await db.SaveChangesAsync();
        return row.UserGroupLocationDefaultId;
    }

    public async Task DeleteStateAsync(int userGroupLocationDefaultId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var record = await db.TblUserGroupLocationDefaults
            .FirstOrDefaultAsync(d => d.UserGroupLocationDefaultId == userGroupLocationDefaultId);
        if (record == null) return;

        db.TblUserGroupLocationDefaults.Remove(record);

        if (record.IsDefaultState)
        {
            var successor = await db.TblUserGroupLocationDefaults
                .Where(d => d.UserGroupId == record.UserGroupId
                         && d.LocationId == record.LocationId
                         && d.UserGroupLocationDefaultId != userGroupLocationDefaultId)
                .OrderBy(d => d.StateName)
                .FirstOrDefaultAsync();
            if (successor != null)
            {
                successor.IsDefaultState = true;
                successor.StateScheduleJson = null;
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task SetDefaultStateAsync(int userGroupLocationDefaultId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var record = await db.TblUserGroupLocationDefaults
            .FirstOrDefaultAsync(d => d.UserGroupLocationDefaultId == userGroupLocationDefaultId);
        if (record == null || record.IsDefaultState) return;

        var siblings = await db.TblUserGroupLocationDefaults
            .Where(d => d.UserGroupId == record.UserGroupId && d.LocationId == record.LocationId
                     && d.UserGroupLocationDefaultId != userGroupLocationDefaultId)
            .ToListAsync();
        foreach (var sibling in siblings)
            sibling.IsDefaultState = false;

        record.IsDefaultState = true;
        record.StateScheduleJson = null;
        await db.SaveChangesAsync();
    }

    public async Task SyncStateContentAsync(int sourceRowId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var source = await db.TblUserGroupLocationDefaults
            .AsNoTracking()
            .Include(d => d.Logos)
            .Include(d => d.LayerContents)
            .FirstOrDefaultAsync(d => d.UserGroupLocationDefaultId == sourceRowId);
        if (source == null) return;

        var siblings = await db.TblUserGroupLocationDefaults
            .Include(d => d.Logos)
            .Include(d => d.LayerContents)
            .Where(d => d.UserGroupId == source.UserGroupId && d.LocationId == source.LocationId
                     && d.UserGroupLocationDefaultId != sourceRowId)
            .ToListAsync();

        foreach (var target in siblings)
        {
            target.DefaultConstraintId = source.DefaultConstraintId;
            target.BackgroundPath = source.BackgroundPath;
            target.BackgroundMode = source.BackgroundMode;
            target.BackgroundOpacityPct = source.BackgroundOpacityPct;
            target.MaskPath = source.MaskPath;
            target.MaskMode = source.MaskMode;
            target.MaskOpacityPct = source.MaskOpacityPct;
            target.LogoPath = source.LogoPath;
            target.LastEditDate = DateTime.UtcNow;

            target.Logos.Clear();
            target.LayerContents.Clear();
            CopyChildren(source, target);
        }

        await db.SaveChangesAsync();
    }

    private static void CopyChildren(TblUserGroupLocationDefault source, TblUserGroupLocationDefault target)
    {
        foreach (var logo in source.Logos)
        {
            target.Logos.Add(new TblUserGroupLocationLogo
            {
                LogoRoleKey = logo.LogoRoleKey,
                ContentPath = logo.ContentPath,
                ContentMode = logo.ContentMode,
                CreateDate = DateTime.UtcNow,
                LastEditDate = DateTime.UtcNow,
            });
        }

        foreach (var seg in source.LayerContents.OrderBy(s => s.SortOrder))
        {
            target.LayerContents.Add(new TblUserGroupLocationLayerContent
            {
                LayerRole = seg.LayerRole,
                SortOrder = seg.SortOrder,
                ContentPath = seg.ContentPath,
                IntrinsicWidthPx = seg.IntrinsicWidthPx,
                ContentMode = seg.ContentMode,
                CreateDate = DateTime.UtcNow,
                LastEditDate = DateTime.UtcNow,
            });
        }
    }

    public async Task DeleteAsync(int userGroupLocationDefaultId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var record = await db.TblUserGroupLocationDefaults
            .FirstOrDefaultAsync(d => d.UserGroupLocationDefaultId == userGroupLocationDefaultId);

        if (record != null)
        {
            db.TblUserGroupLocationDefaults.Remove(record);
            await db.SaveChangesAsync();
        }
    }
}
