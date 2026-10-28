using System.Text.Json.Serialization;

namespace DXSuite.Director.Models
{
    public class InkScheduleEntry
    {
        [JsonPropertyName("startDate")]
        public string? StartDate { get; set; }

        [JsonPropertyName("endDate")]
        public string? EndDate { get; set; }

        [JsonPropertyName("allDay")]
        public bool AllDay { get; set; }

        [JsonPropertyName("startTime")]
        public string? StartTime { get; set; }

        [JsonPropertyName("endTime")]
        public string? EndTime { get; set; }

        [JsonPropertyName("days")]
        public List<string>? Days { get; set; }

        private static readonly List<string> AllDays = new() { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };

        /// <summary>
        /// Returns a normalized copy with all fields populated for the Ink API.
        /// Ink requires every field present — missing fields are rendered as "Invalid date".
        /// </summary>
        public InkScheduleEntry ToInkFormat()
        {
            var hasDateRange = !string.IsNullOrEmpty(StartDate) && !string.IsNullOrEmpty(EndDate);
            var hasTimes = !string.IsNullOrEmpty(StartTime) && !string.IsNullOrEmpty(EndTime);

            return new InkScheduleEntry
            {
                StartDate = !string.IsNullOrEmpty(StartDate) ? StartDate : "1970-01-01",
                EndDate = !string.IsNullOrEmpty(EndDate) ? EndDate : "2099-12-31",
                AllDay = !hasTimes,
                StartTime = hasTimes ? StartTime : "00:00:00",
                EndTime = hasTimes ? EndTime : "23:59:59",
                Days = Days != null && Days.Count > 0 ? Days : AllDays
            };
        }
    }

    public class InkSyncResult
    {
        public int Uploaded { get; set; }
        public int Patched { get; set; }
        public int Deleted { get; set; }
        public int Errors { get; set; }
    }
}
