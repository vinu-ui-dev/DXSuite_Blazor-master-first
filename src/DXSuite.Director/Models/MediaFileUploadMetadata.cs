using System.Collections.Generic;
namespace DXSuite.Director.Models
{
    public class MediaFileUploadMetadata
    {
        public DateOnly? StartDate { get; set; }
        public TimeOnly? StartTime { get; set; }
        public DateOnly? EndDate { get; set; }
        public TimeOnly? EndTime { get; set; }
        public string? LanguageCode { get; set; }
        public List<string>? Days { get; set; }
    }
}
