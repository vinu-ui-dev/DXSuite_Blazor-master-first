using System.Xml.Linq;
using DXSuite.Director.Models.BrightSign;

namespace DXSuite.Director.Services
{
    public class MetadataParser
    {
        private readonly string _siteUrl;

        public MetadataParser()
        {
            //_siteUrl = siteUrl;
        }

        public Dictionary<string, PlayerContentItem> ParseAllMetadata(string directory)
        {
            var map = new Dictionary<string, PlayerContentItem>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(directory)) return map;

            foreach (var metadataPath in Directory.GetFiles(directory, "*.metadata"))
            {
                try
                {
                    var xml = File.ReadAllText(metadataPath);
                    var xdoc = XDocument.Parse(xml);

                    var fields = xdoc.Descendants("field").ToDictionary(
                        f => f.Attribute("name")?.Value ?? "",
                        f => f.Attribute("value")?.Value ?? ""
                    );

                    // Derive key from filename itself (no longer using <FileName> from inside XML)
                    var actualFilename = Path.GetFileNameWithoutExtension(metadataPath);

                    DateTime? startDate = DateTime.TryParse(fields.GetValueOrDefault("StartDate"), out var sDate) ? sDate : null;
                    DateTime? endDate = DateTime.TryParse(fields.GetValueOrDefault("EndDate"), out var eDate) ? eDate : null;
                    TimeSpan? startTime = TimeSpan.TryParse(fields.GetValueOrDefault("StartTime"), out var sTime) ? sTime : null;
                    TimeSpan? endTime = TimeSpan.TryParse(fields.GetValueOrDefault("EndTime"), out var eTime) ? eTime : null;

                    var days = fields.GetValueOrDefault("Days");
                    int? weight = int.TryParse(fields.GetValueOrDefault("Weight"), out var w)
                        ? Math.Clamp(w, 1, 10) : null;

                    map[actualFilename] = new PlayerContentItem
                    {
                        FileName = actualFilename,
                        StartDate = startDate,
                        EndDate = endDate,
                        StartTime = startTime,
                        EndTime = endTime,
                        Days = !string.IsNullOrEmpty(days) ? days : null,
                        Weight = weight
                    };
                }
                catch (Exception ex)
                {
                    // Metadata parse errors are expected for malformed XML; swallow silently
                }
            }

            return map;
        }
    }
}
