using DXSuite.Connectors.Services.CoreApi.Models;
using DXSuite.Shared.Audit;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.CoreApi;

/// <summary>
/// The Flight Data Editor field settings as the settings page shows them, and the <c>fde.settings-changed</c> events
/// (AUD-08) a save writes: one per setting changed, with its old and new value.
/// </summary>
public static class FieldSettingsAudit
{
    // CodeSharesModify is not offered (FDE-05), so a save never changes it.
    private static readonly (string Name, Func<UserSettings, bool?> Read)[] Settings =
    [
        (nameof(UserSettings.GATModify), s => s.GATModify),
        (nameof(UserSettings.ETModify), s => s.ETModify),
        (nameof(UserSettings.ATModify), s => s.ATModify),
        (nameof(UserSettings.CARModify), s => s.CARModify),
        (nameof(UserSettings.RCPModify), s => s.RCPModify),
        (nameof(UserSettings.ROPModify), s => s.ROPModify),
        (nameof(UserSettings.ROBModify), s => s.ROBModify),
        (nameof(UserSettings.ROSModify), s => s.ROSModify),
        (nameof(UserSettings.ROTModify), s => s.ROTModify),
        (nameof(UserSettings.PreserveAllOverridesOption), s => s.PreserveAllOverridesOption),
    ];

    /// <summary>
    /// Each setting's value as the page shows it: unset reads as off, so a save that only writes an unset setting
    /// as off changes nothing a user saw.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> Snapshot(UserSettings settings) =>
        Settings.ToDictionary(s => s.Name, s => s.Read(settings) ?? false);

    /// <summary>Writes one event per setting that differs between <paramref name="saved"/> and <paramref name="sent"/>.</summary>
    /// <param name="log">Null when the host has no User Event Log; nothing is written.</param>
    public static async Task WriteAsync<T>(IUserEventLog? log, ILogger logger, IReadOnlyDictionary<string, bool> saved,
        UserSettings sent, CoreApiResult<T> result)
    {
        foreach (var (name, now) in Snapshot(sent))
        {
            if (saved.TryGetValue(name, out var was) && was == now) continue;
            var details = new Dictionary<string, object?> { ["setting"] = name, ["old"] = was, ["new"] = now };
            CoreApiEventOutcome.AddError(details, result);
            var userEvent = new UserEvent(UserEventActionKeys.FdeSettingsChanged,
                new UserEventTarget(UserEventTargetTypes.CoreApiSettings, null, "Flight Data Editor field settings"),
                CoreApiEventOutcome.Of(result), details);
            await log.TryWriteAsync(logger, userEvent.ActionKey, (l, ct) => l.WriteAsync(userEvent, ct));
        }
    }
}
