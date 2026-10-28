namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblFlight
    {
        public int FlightRecordId { get; set; }
        public string? Flt { get; set; }
        public string? Flc { get; set; }
        public string? Fln { get; set; }
        public string? Org { get; set; }
        public DateTime? Sta { get; set; }
        public DateTime? Eta { get; set; }
        public DateTime? Ata { get; set; }
        public DateTime? FirstBag { get; set; }
        public DateTime? LastBag { get; set; }
        public string? Rcp { get; set; }
        public string? Bhscar { get; set; }
        public string? Car { get; set; }
        public string? Cpr { get; set; }
        public string? Gat { get; set; }
        public string? Tef { get; set; }
        public bool LastBagDelayActive { get; set; }
        public string? AircraftType { get; set; }
        public DateTime? LastModifiedDate { get; set; }
        public DateTime? CreateDate { get; set; }
        public virtual TblCarousel? CarNavigation { get; set; }
        public virtual TblAirline? FlcNavigation { get; set; }
    }
}