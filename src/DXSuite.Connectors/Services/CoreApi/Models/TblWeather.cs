namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblWeather
    {
        public string IataCode { get; set; } = null!;
        public string? RadarStation { get; set; }
        public double? TemperatureF { get; set; }
        public double? TemperatureC { get; set; }
        public double? Humidity { get; set; }
        public string? Description { get; set; }
        public string? WindDirection { get; set; }
        public string? WindSpeed { get; set; }
        public string? Icon { get; set; }
        public int CallCount { get; set; }
        public DateTime? StationUpdateTime { get; set; }
        public string? ForecastUri { get; set; }
        public double? ForecastTemperatureF { get; set; }
        public double? ForecastTemperatureC { get; set; }
        public string? ForecastDescription { get; set; }
        public string? ForecastDescriptionDetailed { get; set; }
        public string? ForecastWindDirection { get; set; }
        public string? ForecastWindSpeed { get; set; }
        public string? ForecastPeriodName { get; set; }
        public string? ForecastTemperatureTrend { get; set; }
        public string? ForecastIcon { get; set; }
        public int ForecastCallCount { get; set; }
        public DateTime? ForecastStationUpdateTime { get; set; }
        public DateTime? SyncTime { get; set; }
        public DateTime CreateDate { get; set; }
        public virtual TblLocation IataCodeNavigation { get; set; } = null!;
    }
}