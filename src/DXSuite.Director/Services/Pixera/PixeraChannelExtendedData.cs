using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.Pixera;

/// <summary>
/// Merge helpers for the Pixera-owned keys inside TblContentChannel.ExtendedData.
/// ExtendedData is a shared JSON document with several concurrent writers: the channel
/// settings modal (pixeraLayerSettings), PixeraContentSyncService (pixeraHandleMap) and
/// PixeraLayerSyncService (pixeraLayerSettings.lastSyncedAt). Sync cycles hold channel
/// rows for seconds while talking to Pixera, so a writer must never persist the whole
/// document from its cycle-start snapshot — that would silently revert any save that
/// landed mid-cycle (e.g. an admin disabling transportEnabled, which re-enables live
/// layer takeover). Instead, re-read the current database value at write time and merge
/// in only the keys the writer owns.
/// </summary>
public static class PixeraChannelExtendedData
{
    /// <summary>
    /// Reads the channel's current ExtendedData straight from the database, bypassing
    /// any tracked snapshot held since the start of a sync cycle.
    /// </summary>
    public static Task<string?> ReadCurrentAsync(IDirectorDBContext db, int channelId, CancellationToken ct)
        => db.TblContentChannels
            .AsNoTracking()
            .Where(c => c.ChannelId == channelId)
            .Select(c => c.ExtendedData)
            .FirstOrDefaultAsync(ct);

    /// <summary>Sets pixeraLayerSettings.lastSyncedAt, preserving every other property.</summary>
    public static string WithLayerSyncTimestamp(string? extendedData, DateTime syncedAt)
    {
        var root = ParseOrEmpty(extendedData);
        var settings = (root["pixeraLayerSettings"] as JObject) ?? new JObject();
        settings["lastSyncedAt"]    = syncedAt.ToString("O");
        root["pixeraLayerSettings"] = settings;
        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>Replaces pixeraHandleMap, preserving every other property.</summary>
    public static string WithHandleMap(string? extendedData, JObject handleMap)
    {
        var root = ParseOrEmpty(extendedData);
        root["pixeraHandleMap"] = handleMap;
        return root.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JObject ParseOrEmpty(string? extendedData)
    {
        if (string.IsNullOrEmpty(extendedData)) return new JObject();
        try { return JObject.Parse(extendedData); }
        catch { return new JObject(); }
    }
}
