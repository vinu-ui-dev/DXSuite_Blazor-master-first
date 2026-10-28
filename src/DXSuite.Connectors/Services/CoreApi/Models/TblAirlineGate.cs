namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblAirlineGate
    {
        public string Gat { get; set; } = null!;
        public string AirlineCode { get; set; } = null!;
        public string? WebLink { get; set; }
        public bool DisableGids { get; set; }
        public virtual TblAirline AirlineCodeNavigation { get; set; } = null!;
        public virtual TblGate GatNavigation { get; set; } = null!;
    }
}
