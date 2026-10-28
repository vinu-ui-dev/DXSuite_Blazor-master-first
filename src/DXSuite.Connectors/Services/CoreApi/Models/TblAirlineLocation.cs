namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblAirlineLocation
    {
        public string AirlineCode { get; set; } = null!;
        public string IataCode { get; set; } = null!;
        public virtual TblLocation IataCodeNavigation { get; set; } = null!;
    }
}