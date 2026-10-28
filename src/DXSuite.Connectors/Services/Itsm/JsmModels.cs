namespace DXSuite.Connectors.Services.Itsm;

public class JsmServiceDesk
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectKey { get; set; } = string.Empty;
}

public class JsmRequestType
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string HelpText { get; set; } = string.Empty;
    public string IssueTypeId { get; set; } = string.Empty;
    public List<string> GroupIds { get; set; } = new();
}

public class JsmConnectionConfig
{
    public string ServiceDeskId { get; set; } = string.Empty;
    public string ServiceDeskName { get; set; } = string.Empty;
    public string ProjectKey { get; set; } = string.Empty;
    public List<JsmRequestType> RequestTypes { get; set; } = new();
    public string? DefaultRequestTypeId { get; set; }
}
