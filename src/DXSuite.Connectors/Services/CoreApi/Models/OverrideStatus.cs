namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public class OverrideStatus
    {
        public bool feedOverride { get; set; }
        public string? arrivalsFeedStatus { get; set; }
        public string? departuresFeedStatus { get; set; }
    }
}
