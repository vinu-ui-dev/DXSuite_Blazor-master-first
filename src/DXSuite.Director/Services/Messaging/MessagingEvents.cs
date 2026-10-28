using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The targets and shared detail shapes of the Messaging and Display Zone User events (AUD-08), so every service names
/// a Message, an Activation, a Message Type or a Display Zone the same way.
/// </summary>
internal static class MessagingEvents
{
    public static UserEventTarget Message(int? messageId, string? name) =>
        new(UserEventTargetTypes.Message, messageId?.ToString(), name);

    public static UserEventTarget Activation(int activationId, string? messageName) =>
        new(UserEventTargetTypes.Activation, activationId, messageName);

    public static UserEventTarget MessageType(int? messageTypeId, string? displayName) =>
        new(UserEventTargetTypes.MessageType, messageTypeId?.ToString(), displayName);

    public static UserEventTarget DisplayZone(int? zoneId, string? name) =>
        new(UserEventTargetTypes.DisplayZone, zoneId?.ToString(), name);

    /// <summary>A Message's texts by language code, in code order, as the <c>texts</c> detail of AUD-08.</summary>
    public static SortedDictionary<string, object?> Texts(IEnumerable<(string Code, string Body)> texts) =>
        new(texts.ToDictionary(t => t.Code, t => (object?)t.Body, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    /// <summary><c>message.created</c> in the AUD-08 shape.</summary>
    public static UserEvent Created(TblMessage message, string messageTypeCode) => new(
        UserEventActionKeys.MessageCreated,
        Message(message.MessageId, message.Name),
        Details: new
        {
            messageTypeCode,
            isCanned = message.IsCanned,
            status = message.Status.ToString(),
            texts = Texts(message.Texts.Select(t => (t.LanguageCode, t.Body))),
        });

    /// <summary>
    /// Adds <paramref name="field"/> to <paramref name="changes"/> when any entry differs, with only the entries that
    /// changed (an added or removed entry reads as null on the other side), so an edit of one language's text logs
    /// that language alone.
    /// </summary>
    public static void ChangedEntries(UserEventChanges changes, string field,
        IReadOnlyDictionary<string, object?> before, IReadOnlyDictionary<string, object?> after)
    {
        var keys = before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(k => !Equals(before.GetValueOrDefault(k), after.GetValueOrDefault(k)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0) return;
        changes.Always(field,
            keys.ToDictionary(k => k, k => before.GetValueOrDefault(k)),
            keys.ToDictionary(k => k, k => after.GetValueOrDefault(k)));
    }
}
