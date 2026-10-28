using Microsoft.AspNetCore.Mvc;
using DXSuite.Director.Models.Weather;
using DXSuite.Director.Services;

namespace DXSuite.Director.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WeatherController : ControllerBase
    {
        private readonly IWeatherService _weatherService;
        private readonly ILogger<WeatherController> _logger;

        public WeatherController(IWeatherService weatherService, ILogger<WeatherController> logger)
        {
            _weatherService = weatherService;
            _logger = logger;
        }

        /// <summary>
        /// Gets weather forecast for a US zip code
        /// </summary>
        /// <param name="zipCode">5-digit US zip code</param>
        /// <returns>Weather forecast with 5-day outlook</returns>
        [HttpGet]
        public async Task<ActionResult<WeatherResponse>> GetWeather([FromQuery] string zipCode)
        {
            if (string.IsNullOrWhiteSpace(zipCode))
            {
                return BadRequest(new { error = "zipCode parameter is required" });
            }

            if (zipCode.Length != 5 || !zipCode.All(char.IsDigit))
            {
                return BadRequest(new { error = "zipCode must be a 5-digit number" });
            }

            var weather = await _weatherService.GetWeatherAsync(zipCode);

            if (weather == null)
            {
                return NotFound(new { error = $"Could not fetch weather for zip code {zipCode}" });
            }

            return Ok(weather);
        }

        /// <summary>
        /// Health check endpoint for weather service
        /// </summary>
        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok(new { status = "healthy", service = "weather" });
        }
    }
}
