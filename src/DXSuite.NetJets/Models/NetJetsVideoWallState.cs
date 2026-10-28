namespace DXSuite.NetJets.Models;

/// <summary>
/// State blob stored in tblLocation.ExtendedData for the NetJets video wall location.
/// Single instance per deployment; the location's own row is the storage key.
/// </summary>
public class NetJetsVideoWallState
{
    public const int DetailMessageCount = 3;

    public string MainMessage { get; set; } = string.Empty;

    public List<string> DetailMessages { get; set; } = new List<string> { string.Empty, string.Empty, string.Empty };

    public bool MessagesEnabled { get; set; }

    public bool FullScreenTv { get; set; }

    public bool SpecialContentTakeover { get; set; }

    /// <summary>
    /// Returns a normalized copy: empty/whitespace detail entries are dropped,
    /// then the list is right-padded back to <see cref="DetailMessageCount"/> with empty strings.
    /// Position-3-only content becomes position-1, matching CBPMessages compaction behavior
    /// that Moxie expects on the wire.
    /// </summary>
    public NetJetsVideoWallState Compact()
    {
        var nonEmpty = (DetailMessages ?? new List<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .ToList();

        while (nonEmpty.Count < DetailMessageCount)
        {
            nonEmpty.Add(string.Empty);
        }

        return new NetJetsVideoWallState
        {
            MainMessage = MainMessage?.Trim() ?? string.Empty,
            DetailMessages = nonEmpty.Take(DetailMessageCount).ToList(),
            MessagesEnabled = MessagesEnabled,
            FullScreenTv = FullScreenTv,
            SpecialContentTakeover = SpecialContentTakeover
        };
    }
}
