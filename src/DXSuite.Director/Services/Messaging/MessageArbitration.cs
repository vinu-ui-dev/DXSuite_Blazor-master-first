using DXSuite.Director.Entities;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.Messaging;

/// <summary>An Activation that won its Screen Part, with the effective rank arbitration used for it (MSG-10).</summary>
public sealed record ArbitratedActivation(TblMessageActivation Activation, int Rank);

/// <summary>One display's arbitration result: each Screen Part's entries in the order they are published.</summary>
public sealed record DisplayArbitration(
    IReadOnlyList<ArbitratedActivation> FullScreen,
    IReadOnlyList<ArbitratedActivation> Overlay,
    IReadOnlyList<ArbitratedActivation> Ticker);

/// <summary>
/// Decides what each Screen Part of one display shows (MSG-07 to MSG-10). Pure: the caller passes the Activations
/// live on the display at the moment it is publishing for (MSG-17), each with its Message and Message Type loaded.
/// The type's Priority is read on every call, so a reorder re-ranks Activations without an override at once.
/// "Newest" is the most recently sent (<see cref="TblMessageActivation.ActivatedUtc"/>): it does not change with a
/// weekly window's daily occurrences, so a Rotate set stays stable until one of its Activations ends.
/// </summary>
public sealed class MessageArbitration(IOptions<MessagingOptions> options)
{
    /// <summary>A part publishes at most this many entries; it is also the slot count per part in Ink (INK-04).</summary>
    public const int MaxRotating = 3;

    public DisplayArbitration Arbitrate(IEnumerable<TblMessageActivation> liveOnDisplay)
    {
        var live = liveOnDisplay.ToList();
        return new DisplayArbitration(
            ArbitratePart(live, MessageDisplayMode.FullScreen),
            ArbitratePart(live, MessageDisplayMode.Overlay),
            ArbitratePart(live, MessageDisplayMode.Ticker));
    }

    private IReadOnlyList<ArbitratedActivation> ArbitratePart(List<TblMessageActivation> live, MessageDisplayMode part)
    {
        var ranked = live
            .Where(a => a.Message.MessageType.DisplayMode == part)
            .Select(a => new ArbitratedActivation(a, EffectiveRank(a)))
            .OrderByDescending(a => a.Activation.Message.MessageType.IsTopPriority)
            .ThenBy(a => a.Rank)
            .ThenByDescending(a => a.Activation.ActivatedUtc)
            .ThenByDescending(a => a.Activation.MessageActivationId)
            .ToList();
        if (ranked.Count == 0)
        {
            return [];
        }

        var best = ranked[0];
        var tied = ranked
            .TakeWhile(a => a.Activation.Message.MessageType.IsTopPriority == best.Activation.Message.MessageType.IsTopPriority
                            && a.Rank == best.Rank)
            .Take(options.Value.TieBehaviour == TieBehaviour.Rotate ? MaxRotating : 1)
            .ToList();

        // Newest first picks who shows; the part is then published oldest first, the order Rotate shows them in (INK-04).
        tied.Reverse();
        return tied;
    }

    private static int EffectiveRank(TblMessageActivation activation) =>
        EffectiveRank(activation.PriorityOverride, activation.Message.MessageType);

    /// <summary>An Activation's rank (MSG-09): its priority override when the sender set one, else its type's rank.</summary>
    internal static int EffectiveRank(int? priorityOverride, TblMessageType type) => priorityOverride ?? type.Priority;
}
