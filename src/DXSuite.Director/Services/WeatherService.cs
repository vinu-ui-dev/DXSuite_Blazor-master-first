using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Director.Models.Weather;

namespace DXSuite.Director.Services
{
    public interface IWeatherService
    {
        /// <summary>
        /// Gets weather forecast for a US zip code
        /// </summary>
        Task<WeatherResponse?> GetWeatherAsync(string zipCode);
    }

    public class WeatherService : IWeatherService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<WeatherService> _logger;

        // Cache weather data for 1 hour
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

        // US zip code to coordinates mapping (simplified - production would use a geocoding service)
        private static readonly Dictionary<string, (double Lat, double Lon)> ZipCodeCache = new();
        private static readonly object ZipCodeCacheLock = new();

        public WeatherService(HttpClient httpClient, IMemoryCache cache, ILogger<WeatherService> logger)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;

            // Weather.gov requires a User-Agent header
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "DXSuite/1.0 (contact@example.com)");
            }
        }

        public async Task<WeatherResponse?> GetWeatherAsync(string zipCode)
        {
            if (string.IsNullOrWhiteSpace(zipCode) || zipCode.Length != 5 || !zipCode.All(char.IsDigit))
            {
                _logger.LogWarning("Invalid zip code format: {ZipCode}", zipCode);
                return null;
            }

            var cacheKey = $"weather_{zipCode}";

            // Check cache first
            if (_cache.TryGetValue(cacheKey, out WeatherResponse? cachedResponse))
            {
                _logger.LogDebug("Returning cached weather for {ZipCode}", zipCode);
                return cachedResponse;
            }

            try
            {
                // Get coordinates for zip code
                var coordinates = await GetCoordinatesForZipCode(zipCode);
                if (coordinates == null)
                {
                    _logger.LogWarning("Could not get coordinates for zip code: {ZipCode}", zipCode);
                    return null;
                }

                // Get grid point from Weather.gov
                var pointsUrl = $"https://api.weather.gov/points/{coordinates.Value.Lat},{coordinates.Value.Lon}";
                var pointsResponse = await _httpClient.GetFromJsonAsync<WeatherGovPointsResponse>(pointsUrl);

                if (pointsResponse?.Properties?.Forecast == null)
                {
                    _logger.LogWarning("Could not get forecast URL for zip code: {ZipCode}", zipCode);
                    return null;
                }

                // Get location name
                var locationName = "Unknown";
                if (pointsResponse.Properties.RelativeLocation?.Properties != null)
                {
                    var loc = pointsResponse.Properties.RelativeLocation.Properties;
                    locationName = $"{loc.City}, {loc.State}";
                }

                // Get forecast
                var forecastResponse = await _httpClient.GetFromJsonAsync<WeatherGovForecastResponse>(
                    pointsResponse.Properties.Forecast);

                if (forecastResponse?.Properties?.Periods == null)
                {
                    _logger.LogWarning("Could not get forecast periods for zip code: {ZipCode}", zipCode);
                    return null;
                }

                // Build response
                var response = new WeatherResponse
                {
                    Location = locationName,
                    ZipCode = zipCode,
                    FetchedAt = DateTime.UtcNow,
                    Forecast = BuildForecastFromPeriods(forecastResponse.Properties.Periods)
                };

                // Cache the result
                _cache.Set(cacheKey, response, CacheDuration);

                _logger.LogInformation("Fetched weather for {ZipCode} ({Location})", zipCode, locationName);
                return response;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error fetching weather for {ZipCode}", zipCode);

                // Try to return stale cache if available
                if (_cache.TryGetValue($"weather_{zipCode}_stale", out WeatherResponse? staleResponse))
                {
                    _logger.LogWarning("Returning stale weather data for {ZipCode}", zipCode);
                    return staleResponse;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching weather for {ZipCode}", zipCode);
                return null;
            }
        }

        private List<DailyForecast> BuildForecastFromPeriods(List<WeatherGovPeriod> periods)
        {
            var forecasts = new List<DailyForecast>();
            var processedDays = new HashSet<DateTime>();

            foreach (var period in periods)
            {
                var dayDate = period.StartTime.Date;

                // Skip if we already have this day
                if (processedDays.Contains(dayDate))
                {
                    // Update with nighttime low if this is night period
                    if (!period.IsDaytime)
                    {
                        var existing = forecasts.FirstOrDefault(f => f.Date == dayDate);
                        if (existing != null)
                        {
                            existing.LowTemp = period.Temperature;
                        }
                    }
                    continue;
                }

                // Only start with daytime periods
                if (!period.IsDaytime) continue;

                // Limit to 5 days
                if (forecasts.Count >= 5) break;

                processedDays.Add(dayDate);

                // Find the corresponding night period for the low
                var nightPeriod = periods.FirstOrDefault(p =>
                    p.StartTime.Date == dayDate && !p.IsDaytime);

                forecasts.Add(new DailyForecast
                {
                    DayName = period.Name ?? dayDate.ToString("dddd"),
                    Date = dayDate,
                    HighTemp = period.Temperature,
                    LowTemp = nightPeriod?.Temperature ?? period.Temperature - 15,
                    Condition = GetSimpleCondition(period.ShortForecast ?? ""),
                    IconUrl = period.Icon ?? "",
                    PrecipitationChance = period.ProbabilityOfPrecipitation?.Value ?? 0,
                    ShortForecast = period.ShortForecast ?? ""
                });
            }

            return forecasts;
        }

        private static string GetSimpleCondition(string shortForecast)
        {
            var lower = shortForecast.ToLower();

            if (lower.Contains("sunny") || lower.Contains("clear"))
                return "Sunny";
            if (lower.Contains("partly cloudy") || lower.Contains("partly sunny"))
                return "Partly Cloudy";
            if (lower.Contains("cloudy") || lower.Contains("overcast"))
                return "Cloudy";
            if (lower.Contains("rain") || lower.Contains("shower"))
                return "Rainy";
            if (lower.Contains("thunderstorm") || lower.Contains("storm"))
                return "Stormy";
            if (lower.Contains("snow"))
                return "Snowy";
            if (lower.Contains("fog") || lower.Contains("mist"))
                return "Foggy";
            if (lower.Contains("wind"))
                return "Windy";

            return "Varied";
        }

        private async Task<(double Lat, double Lon)?> GetCoordinatesForZipCode(string zipCode)
        {
            // Check local cache first
            lock (ZipCodeCacheLock)
            {
                if (ZipCodeCache.TryGetValue(zipCode, out var cached))
                {
                    return cached;
                }
            }

            try
            {
                // Use a free geocoding service (Census Bureau)
                var url = $"https://geocoding.geo.census.gov/geocoder/locations/onelineaddress?address={zipCode}&benchmark=Public_AR_Current&format=json";
                var response = await _httpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var results = doc.RootElement
                    .GetProperty("result")
                    .GetProperty("addressMatches");

                if (results.GetArrayLength() == 0)
                {
                    _logger.LogWarning("No geocoding results for zip code: {ZipCode}", zipCode);
                    return null;
                }

                var coords = results[0].GetProperty("coordinates");
                var lat = coords.GetProperty("y").GetDouble();
                var lon = coords.GetProperty("x").GetDouble();

                // Cache the result
                lock (ZipCodeCacheLock)
                {
                    ZipCodeCache[zipCode] = (lat, lon);
                }

                return (lat, lon);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error geocoding zip code: {ZipCode}", zipCode);
                return null;
            }
        }
    }
}
