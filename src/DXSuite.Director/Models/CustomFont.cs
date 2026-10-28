namespace DXSuite.Director.Models
{
    public class CustomFont
    {
        public string Name { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string WebFontPath { get; set; } = string.Empty;
        public string SkiaFontPath { get; set; } = string.Empty;
        public string Weight { get; set; } = "400";
        public string Style { get; set; } = "normal";
    }
}
