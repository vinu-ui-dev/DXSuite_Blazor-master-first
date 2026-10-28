namespace DXSuite.Shared.Services;

/// <summary>
/// Interface for ITSM platform adapters. Each implementation handles creating, updating,
/// and resolving tickets in a specific ITSM platform (Jira SM, ServiceNow, ConnectWise, etc.)
/// </summary>
public interface IItsmAdapter
{
    string PlatformId { get; }
    Task<TicketResult> CreateTicketAsync(TicketRequest request, CancellationToken cancellationToken = default);
    Task<TicketResult> UpdateTicketAsync(string ticketId, TicketUpdate update, CancellationToken cancellationToken = default);
    Task<TicketResult> ResolveTicketAsync(string ticketId, ItsmResolutionInfo resolution, CancellationToken cancellationToken = default);
    Task<TicketResult> AddAttachmentAsync(string ticketId, TicketAttachment attachment, CancellationToken cancellationToken = default);
    Task<TicketInfo> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for resolving the correct ITSM adapter based on customer integration configuration.
/// </summary>
public interface IItsmAdapterFactory
{
    Task<IItsmAdapter?> GetAdapterAsync(string customerId, CancellationToken cancellationToken = default);
    IItsmAdapter? GetAdapterByPlatform(string platformId);
}

public class TicketRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium";
    public string Category { get; set; } = string.Empty;
    public string? FloorplanUrl { get; set; }
    public List<string> AffectedDevices { get; set; } = new();
    public string? LocationName { get; set; }
    public string? CustomerReference { get; set; }
    public Dictionary<string, string> CustomFields { get; set; } = new();
}

public class TicketResult
{
    public bool Success { get; set; }
    public string? TicketId { get; set; }
    public string? TicketUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TicketUpdate
{
    public string? Comment { get; set; }
    public string? Priority { get; set; }
    public string? Status { get; set; }
    public Dictionary<string, string>? UpdatedFields { get; set; }
}

/// <summary>
/// Named ItsmResolutionInfo to avoid namespace collision with other ResolutionInfo types.
/// </summary>
public class ItsmResolutionInfo
{
    public string ResolutionNote { get; set; } = string.Empty;
    public string ResolvedBy { get; set; } = "Sentinel";
    public bool AutoResolved { get; set; }
    public string? ActionsTaken { get; set; }
}

public class TicketAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public class TicketInfo
{
    public string TicketId { get; set; } = string.Empty;
    public string? TicketUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool Found { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
