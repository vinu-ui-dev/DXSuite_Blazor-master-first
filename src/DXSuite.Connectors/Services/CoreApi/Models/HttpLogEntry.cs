namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public class HttpLogEntry
    {
        public int RecordId { get; set; }
        public int? ApiRecordId { get; set; }
        public string RequestorIp { get; set; } = null!;
        public string HttpRequestRoute { get; set; } = null!;
        public string Params { get; set; } = null!;
        public int ErrorCode { get; set; }
        public string ErrorMessage { get; set; } = null!;
        public decimal ResponseTime { get; set; }
        public DateTime CreateDate { get; set; }
    }
}
