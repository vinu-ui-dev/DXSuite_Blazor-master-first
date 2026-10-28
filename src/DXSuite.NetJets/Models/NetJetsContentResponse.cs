namespace DXSuite.NetJets.Models;

/// <summary>
/// API response for GET /api/netjets/GetNetJetsContent. Always returns DetailMessages
/// as a length-3 array post-compaction; Moxie handles its own carousel/loop logic.
/// </summary>
public class NetJetsContentResponse
{
    public int LocationId { get; set; }
    public string MainMessage { get; set; } = string.Empty;
    public List<string> DetailMessages { get; set; } = new List<string> { string.Empty, string.Empty, string.Empty };
    public bool MessagesEnabled { get; set; }
    public bool FullScreenTv { get; set; }
    public bool SpecialContentTakeover { get; set; }
}
