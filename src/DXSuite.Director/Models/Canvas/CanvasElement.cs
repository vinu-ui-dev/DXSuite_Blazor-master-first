namespace DXSuite.Director.Models.Canvas
{
    public abstract class CanvasElement
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int ZIndex { get; set; }
        public bool IsSelected { get; set; }
    }

    public class CanvasBackground : CanvasElement
    {
        public string BackgroundColor { get; set; } = "#FFFFFF";

        public CanvasBackground(double canvasWidth, double canvasHeight)
        {
            Width = canvasWidth;
            Height = canvasHeight;
            X = 0;
            Y = 0;
            ZIndex = 0;
        }
    }

    public class CanvasImage : CanvasElement
    {
        public string FileName { get; set; } = string.Empty;
        public string Base64Data { get; set; } = string.Empty;
        public double OriginalWidth { get; set; }
        public double OriginalHeight { get; set; }
        public double AspectRatio => OriginalWidth / OriginalHeight;
        public double Saturation { get; set; } = 1.0; // 0.0 = grayscale, 1.0 = normal, 2.0 = very saturated

        public void SetSizeWithAspectRatio(double newWidth, double newHeight)
        {
            var widthRatio = newWidth / OriginalWidth;
            var heightRatio = newHeight / OriginalHeight;
            var ratio = Math.Min(widthRatio, heightRatio);

            Width = OriginalWidth * ratio;
            Height = OriginalHeight * ratio;
        }
    }

    public class CanvasColorBlock : CanvasElement
    {
        public string Color { get; set; } = "#000000";
        public double Opacity { get; set; } = 0.5; // 0.0 = transparent, 1.0 = opaque
    }

    public class CanvasText : CanvasElement
    {
        public string Text { get; set; } = "Text";
        public string FontFamily { get; set; } = "Arial";
        public double FontSize { get; set; } = 16;
        public string FontColor { get; set; } = "#000000";
        public bool IsBold { get; set; } = false;
        public bool IsItalic { get; set; } = false;
        public string TextAlign { get; set; } = "left"; // left, center, right

        // Shadow properties
        public bool HasShadow { get; set; } = false;
        public double ShadowOffsetX { get; set; } = 2;
        public double ShadowOffsetY { get; set; } = 2;
        public double ShadowBlur { get; set; } = 4;
        public string ShadowColor { get; set; } = "#000000";
        public double ShadowOpacity { get; set; } = 0.5; // 0.0 to 1.0
    }
}
