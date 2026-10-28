namespace DXSuite.Shared.Models;

public class ExternalIntegrationConfig
{
    public bool Enabled { get; set; }
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "dxsuite-api";
    public string Audience { get; set; } = "signage-player";
    public string ValidationEndpoint { get; set; } = "";
}
