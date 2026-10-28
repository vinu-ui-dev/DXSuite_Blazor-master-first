using System.Text.Json.Serialization;

namespace DXSuite.Director.Models.BrightSign
{
    public class WebContentDefinition
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("duration")]
        public int? Duration { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("startDate")]
        public DateTime? StartDate { get; set; }

        [JsonPropertyName("endDate")]
        public DateTime? EndDate { get; set; }

        [JsonPropertyName("startTime")]
        public TimeSpan? StartTime { get; set; }

        [JsonPropertyName("endTime")]
        public TimeSpan? EndTime { get; set; }
    }
}
