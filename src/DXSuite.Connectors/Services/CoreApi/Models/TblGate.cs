namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblGate
    {
        public string Gat { get; set; } = null!;
        public string Ter { get; set; } = null!;
        public string Orientation { get; set; } = null!;
        public int? SortOrder { get; set; }
        public string Defaultstyle { get; set; } = null!;
        public virtual ICollection<TblAirlineGate> TblAirlineGates { get; set; } = new List<TblAirlineGate>();
    }
}
