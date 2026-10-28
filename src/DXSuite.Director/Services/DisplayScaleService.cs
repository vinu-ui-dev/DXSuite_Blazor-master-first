namespace DXSuite.Director.Services
{
    public class DisplayScaleService
    {
        public double GetScaleFactor(double displayWidth, double displayHeight, string size)
        {
            double maxWidth = 668;
            double maxHeight = 375;

            if (displayWidth > displayHeight)
            {
                if (size == "half")
                {
                    maxWidth = 668;
                    maxHeight = 375;
                }
                else if (size == "full")
                {
                    maxWidth = 1200;
                    maxHeight = 675;
                }
            }
            else
            {
                if (size == "half")
                {
                    maxWidth = 450;
                    maxHeight = 253;
                }
                else if (size == "full")
                {
                    maxWidth = 450;
                    maxHeight = 253;
                }
                else if (size == "override")
                {
                    maxWidth = 300;
                    maxHeight = 169;
                }
            }

            double widthScale = maxWidth / displayWidth;
            double heightScale = maxHeight / displayHeight;

            double scaleFactor = Math.Min(widthScale, heightScale);
            return (scaleFactor > 1) ? 1 : scaleFactor;
        }

        public bool MatchingAspectRatio(double contentWidth, double contentHeight, double expectedWidth, double expectedHeight)
        {
            double contentRatio = Math.Round(contentWidth / contentHeight, 1);
            double expectedRatio = Math.Round(expectedWidth / expectedHeight, 1);

            return contentRatio == expectedRatio;
        }

        // Returns true only when content dimensions are a pixel-perfect match
        public bool ExactResolutionMatch(double contentWidth, double contentHeight, double expectedWidth, double expectedHeight)
        {
            return contentWidth == expectedWidth && contentHeight == expectedHeight;
        }
    }

}
