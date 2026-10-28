namespace DXSuite.Connectors.Services.CoreApi.Models;

public class ApiKeyData
{
    public int RecordID { get; set; }
    public string? Name { get; set; }
    public string? ApiKeyID { get; set; }
    public string? Scope { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public DateTime CreateDate { get; set; }
}

