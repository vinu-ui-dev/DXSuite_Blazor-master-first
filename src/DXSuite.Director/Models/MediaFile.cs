using System.Collections.Generic;
namespace DXSuite.Director.Models
{
    public class MediaFile
    {
        public string WebPath { get; set; }
        public string DiskPath { get; set; }
        public List<MetadataField> Metadata { get; set; } = new List<MetadataField>();
        public string MediaType { get; set; } = "";
        public bool IsExpired { get; set; } = false;
        public DateTime? EndDate { get; set; }
        public string? LanguageCode { get; set; }
        public int? ConstraintId { get; set; }
    }
}
