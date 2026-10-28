namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblFlightStatus
    {
        public string StatusId { get; set; } = null!;
        public string StatusName { get; set; } = null!;
        public bool IsLanded { get; set; }
        public DateTime? CreateDate { get; set; }
    }

}
