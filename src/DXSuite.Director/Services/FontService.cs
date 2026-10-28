using DXSuite.Director.Models;
using SkiaSharp;

namespace DXSuite.Director.Services
{
    public interface IFontService
    {
        Task<List<string>> GetAvailableFontsAsync();
        Task LoadCustomFontsAsync();
        SKTypeface? GetSkiaTypeface(string fontFamily, SKFontStyle style);
        string GetWebFontCss();
        string GetCssFontFamily(string fontName);
    }

    public class FontService : IFontService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<FontService> _logger;
        private readonly Dictionary<string, SKTypeface> _loadedFonts = new();
        private List<CustomFont> _customFonts = new();

        // System fonts that are commonly available
        private readonly List<string> _systemFonts = new()
        {
            "Arial",
            "Times New Roman",
            "Helvetica",
            "Georgia",
            "Verdana"
        };

        public FontService(IConfiguration configuration, ILogger<FontService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<string>> GetAvailableFontsAsync()
        {
            await LoadCustomFontsAsync();

            var fonts = new List<string>();
            fonts.AddRange(_systemFonts);
            fonts.AddRange(_customFonts.Select(f => f.Name));

            return fonts;
        }

        public async Task LoadCustomFontsAsync()
        {
            try
            {
                var customFontsSection = _configuration.GetSection("CustomFonts");
                _customFonts = customFontsSection.Get<List<CustomFont>>() ?? new List<CustomFont>();

                foreach (var font in _customFonts)
                {
                    if (!string.IsNullOrEmpty(font.SkiaFontPath))
                    {
                        var fontPath = Path.Combine("wwwroot", font.SkiaFontPath);
                        if (File.Exists(fontPath))
                        {
                            try
                            {
                                var typeface = SKTypeface.FromFile(fontPath);
                                _loadedFonts[font.Family] = typeface;
                                _logger.LogInformation($"Loaded custom font: {font.Name} from {fontPath}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, $"Failed to load font file: {fontPath}");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Font file not found: {fontPath}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom fonts");
            }

            await Task.CompletedTask;
        }

        public SKTypeface? GetSkiaTypeface(string fontFamily, SKFontStyle style)
        {
            // First check if it's a custom font we've loaded
            if (_loadedFonts.TryGetValue(fontFamily, out var customTypeface))
            {
                return customTypeface;
            }

            // Fall back to system fonts
            return SKTypeface.FromFamilyName(fontFamily, style);
        }

        public string GetWebFontCss()
        {
            var css = "";
            foreach (var font in _customFonts)
            {
                if (!string.IsNullOrEmpty(font.WebFontPath))
                {
                    // Determine format based on file extension
                    var format = font.WebFontPath.ToLower().EndsWith(".woff2") ? "woff2" :
                                font.WebFontPath.ToLower().EndsWith(".woff") ? "woff" :
                                font.WebFontPath.ToLower().EndsWith(".ttf") ? "truetype" : "truetype";

                    css += $@"
@font-face {{
    font-family: '{font.Family}';
    src: url('/{font.WebFontPath}') format('{format}');
    font-weight: {font.Weight};
    font-style: {font.Style};
    font-display: swap;
}}
";
                }
            }
            return css;
        }

        public string GetCssFontFamily(string fontName)
        {
            // Check if it's a custom font
            var customFont = _customFonts.FirstOrDefault(f => f.Name == fontName);
            if (customFont != null)
            {
                return customFont.Family;
            }

            // For system fonts, return the name as-is
            return fontName;
        }
    }
}