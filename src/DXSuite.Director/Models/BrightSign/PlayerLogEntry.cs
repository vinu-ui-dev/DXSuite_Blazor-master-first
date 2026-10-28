namespace DXSuite.Director.Models.BrightSign
{
    public class PlayerLogEntry
    {
        public string SerialNumber { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Event { get; set; } = string.Empty;
    }
}
