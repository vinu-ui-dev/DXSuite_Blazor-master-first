namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblLocation
    {
        public string IataCode { get; set; } = null!;
        public string? IcaoCode { get; set; }
        public string? Name { get; set; }
        public string? Country { get; set; }
        public string? CountrySymbol { get; set; }
        public string? FlightSector { get; set; }
        public double? AltLanguage { get; set; }
        public string LanguageCodes { get; set; } = null!;
        public string? ElapsedTimeJet { get; set; }
        public string? ElapsedTimeProp { get; set; }
        public string? Description { get; set; }
        public string? Maxflts { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public bool WeatherUnavailable { get; set; }
        public bool PullForecast { get; set; }
        public bool DisplayWithinDsc { get; set; }
        public DateTime? CreateDate { get; set; }
        public virtual ICollection<TblAirlineLocation> TblAirlineLocations { get; set; } = new List<TblAirlineLocation>();
        public virtual TblWeather? TblWeather { get; set; }
    }
}