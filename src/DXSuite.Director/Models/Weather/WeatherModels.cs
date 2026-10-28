namespace DXSuite.Director.Models.Weather
{
    /// <summary>
    /// Weather response containing location and forecast data
    /// </summary>
    public class WeatherResponse
    {
        public string Location { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
        public List<DailyForecast> Forecast { get; set; } = new();
    }

    /// <summary>
    /// Single day forecast data
    /// </summary>
    public class DailyForecast
    {
        public string DayName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public int HighTemp { get; set; }
        public int LowTemp { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public int PrecipitationChance { get; set; }
        public string ShortForecast { get; set; } = string.Empty;
    }

    /// <summary>
    /// Internal model for Weather.gov points API response
    /// </summary>
    internal class WeatherGovPointsResponse
    {
        public WeatherGovProperties? Properties { get; set; }
    }

    internal class WeatherGovProperties
    {
        public string? Forecast { get; set; }
        public string? ForecastHourly { get; set; }
        public WeatherGovRelativeLocation? RelativeLocation { get; set; }
    }

    internal class WeatherGovRelativeLocation
    {
        public WeatherGovRelativeLocationProperties? Properties { get; set; }
    }

    internal class WeatherGovRelativeLocationProperties
    {
        public string? City { get; set; }
        public string? State { get; set; }
    }

    /// <summary>
    /// Internal model for Weather.gov forecast API response
    /// </summary>
    internal class WeatherGovForecastResponse
    {
        public WeatherGovForecastProperties? Properties { get; set; }
    }

    internal class WeatherGovForecastProperties
    {
        public List<WeatherGovPeriod>? Periods { get; set; }
    }

    internal class WeatherGovPeriod
    {
        public int Number { get; set; }
        public string? Name { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsDaytime { get; set; }
        public int Temperature { get; set; }
        public string? TemperatureUnit { get; set; }
        public string? ShortForecast { get; set; }
        public string? DetailedForecast { get; set; }
        public string? Icon { get; set; }
        public WeatherGovProbabilityOfPrecipitation? ProbabilityOfPrecipitation { get; set; }
    }

    internal class WeatherGovProbabilityOfPrecipitation
    {
        public int? Value { get; set; }
    }
}
