namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public class FeedStatus
    {
        public string? Status { get; set; }
        public DateTime? ArrivalsLast { get; set; }
        public string? ArrivalsStatus { get; set; }
        public DateTime? DeparturesLast { get; set; }
        public string? DeparturesStatus { get; set; }
    }
}
