namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblAirline
    {
        public int RecordId { get; set; }
        public string AirlineCode { get; set; } = null!;
        public string? Icao { get; set; }
        public string? CarrierName { get; set; }
        public bool DisplayWithinSettingsMenu { get; set; }
        public int LastBagExpirationMinutes { get; set; }
        public int LastBagDelaySeconds { get; set; }
        public int CbidiconWidth { get; set; }
        public string? CarrierLogoPath { get; set; }
        public int? CarrierStatus1 { get; set; }
        public int? CarrierStatus2 { get; set; }
        public bool? Tpenabled1 { get; set; }
        public bool? Tpenabled2 { get; set; }
        public int? Pincode { get; set; }
        public DateTime? CreateDate { get; set; }
        public virtual ICollection<TblAirlineGate> TblAirlineGates { get; set; } = new List<TblAirlineGate>();
        public virtual ICollection<TblFlight> TblFlights { get; set; } = new List<TblFlight>();
    }

}