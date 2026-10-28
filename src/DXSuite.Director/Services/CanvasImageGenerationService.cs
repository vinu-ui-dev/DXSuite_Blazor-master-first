using SkiaSharp;
using DXSuite.Director.Models.Canvas;
using System.IO;

namespace DXSuite.Director.Services
{
    public class CanvasImageGenerationService
    {
        private readonly ILogger<CanvasImageGenerationService> _logger;
        private readonly IFontService _fontService;

        public CanvasImageGenerationService(ILogger<CanvasImageGenerationService> logger, IFontService fontService)
        {
            _logger = logger;
            _fontService = fontService;
        }

        public async Task<byte[]> GenerateImageAsync(CanvasProject project, bool includeBackground = true)
        {
            try
            {
                var info = new SKImageInfo((int)project.CanvasWidth, (int)project.CanvasHeight);
                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                var elements = project.GetElementsOrderedByZIndex(includeBackground);

                foreach (var element in elements)
                {
                    switch (element)
                    {
                        case CanvasBackground background:
                            await DrawBackground(canvas, background);
                            break;
                        case CanvasImage image:
                            await DrawImageWithClipping(canvas, image, project);
                            break;
                        case CanvasColorBlock colorBlock:
                            await DrawColorBlock(canvas, colorBlock);
                            break;
                        case CanvasText text:
                            await DrawTextWithClipping(canvas, text, project);
                            break;
                    }
                }

                using var snapshot = surface.Snapshot();
                using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
                return data.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating canvas image");
                throw;
            }
        }

        private async Task DrawBackground(SKCanvas canvas, CanvasBackground background)
        {
            if (SKColor.TryParse(background.BackgroundColor, out var color))
            {
                canvas.Clear(color);
            }
            else
            {
                canvas.Clear(SKColors.White);
            }
            await Task.CompletedTask;
        }

        private async Task DrawColorBlock(SKCanvas canvas, CanvasColorBlock colorBlock)
        {
            try
            {
                if (SKColor.TryParse(colorBlock.Color, out var color))
                {
                    // Apply opacity to the color
                    var alphaColor = color.WithAlpha((byte)(255 * colorBlock.Opacity));

                    using var paint = new SKPaint
                    {
                        Color = alphaColor,
                        IsAntialias = true
                    };

                    var rect = new SKRect(
                        (float)colorBlock.X,
                        (float)colorBlock.Y,
                        (float)(colorBlock.X + colorBlock.Width),
                        (float)(colorBlock.Y + colorBlock.Height)
                    );

                    canvas.DrawRect(rect, paint);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to draw color block");
            }
            await Task.CompletedTask;
        }

        private async Task DrawImage(SKCanvas canvas, CanvasImage canvasImage)
        {
            try
            {
                var imageBytes = Convert.FromBase64String(canvasImage.Base64Data.Split(',').Last());
                using var skImage = SKImage.FromEncodedData(imageBytes);

                if (skImage != null)
                {
                    // Calculate aspect ratio preserving dimensions (like CSS object-fit: contain)
                    var containerWidth = canvasImage.Width;
                    var containerHeight = canvasImage.Height;
                    var imageWidth = skImage.Width;
                    var imageHeight = skImage.Height;

                    var scaleX = containerWidth / imageWidth;
                    var scaleY = containerHeight / imageHeight;
                    var scale = Math.Min(scaleX, scaleY); // Use smaller scale to maintain aspect ratio

                    var scaledWidth = imageWidth * scale;
                    var scaledHeight = imageHeight * scale;

                    // Center the image within the container
                    var offsetX = (containerWidth - scaledWidth) / 2;
                    var offsetY = (containerHeight - scaledHeight) / 2;

                    var destRect = new SKRect(
                        (float)(canvasImage.X + offsetX),
                        (float)(canvasImage.Y + offsetY),
                        (float)(canvasImage.X + offsetX + scaledWidth),
                        (float)(canvasImage.Y + offsetY + scaledHeight)
                    );

                    // Apply saturation filter if needed
                    if (Math.Abs(canvasImage.Saturation - 1.0) > 0.01) // Only apply if saturation is not 1.0
                    {
                        using var paint = new SKPaint();

                        // Create saturation color filter
                        float[] colorMatrix = {
                            0.213f + 0.787f * (float)canvasImage.Saturation, 0.715f - 0.715f * (float)canvasImage.Saturation, 0.072f - 0.072f * (float)canvasImage.Saturation, 0, 0,
                            0.213f - 0.213f * (float)canvasImage.Saturation, 0.715f + 0.285f * (float)canvasImage.Saturation, 0.072f - 0.072f * (float)canvasImage.Saturation, 0, 0,
                            0.213f - 0.213f * (float)canvasImage.Saturation, 0.715f - 0.715f * (float)canvasImage.Saturation, 0.072f + 0.928f * (float)canvasImage.Saturation, 0, 0,
                            0, 0, 0, 1, 0
                        };

                        paint.ColorFilter = SKColorFilter.CreateColorMatrix(colorMatrix);
                        canvas.DrawImage(skImage, destRect, paint);
                    }
                    else
                    {
                        canvas.DrawImage(skImage, destRect);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to draw image: {FileName}", canvasImage.FileName);
            }
            await Task.CompletedTask;
        }

        private async Task DrawText(SKCanvas canvas, CanvasText canvasText)
        {
            try
            {
                using var paint = new SKPaint();

                if (SKColor.TryParse(canvasText.FontColor, out var textColor))
                {
                    paint.Color = textColor;
                }
                else
                {
                    paint.Color = SKColors.Black;
                }

                paint.IsAntialias = true;
                paint.TextSize = (float)canvasText.FontSize;

                var fontStyle = SKFontStyle.Normal;
                if (canvasText.IsBold && canvasText.IsItalic)
                    fontStyle = SKFontStyle.BoldItalic;
                else if (canvasText.IsBold)
                    fontStyle = SKFontStyle.Bold;
                else if (canvasText.IsItalic)
                    fontStyle = SKFontStyle.Italic;

                // Get the correct font family name for SkiaSharp (use CSS family for custom fonts)
                var fontFamilyName = _fontService.GetCssFontFamily(canvasText.FontFamily);
                var typeface = _fontService.GetSkiaTypeface(fontFamilyName, fontStyle) ?? SKTypeface.FromFamilyName(fontFamilyName, fontStyle);
                paint.Typeface = typeface;

                // Ensure bold and italic are applied even if the typeface doesn't support them
                if (canvasText.IsBold && (typeface == null || !typeface.IsBold))
                {
                    paint.FakeBoldText = true;
                }

                if (canvasText.IsItalic && (typeface == null || !typeface.IsItalic))
                {
                    paint.TextSkewX = -0.25f; // Standard skew for italic effect
                }

                // Wrap text to fit within the element width
                var lines = WrapText(canvasText.Text, (float)canvasText.Width, paint);

                // Calculate line height (similar to CSS line-height: 1.2)
                var lineHeight = paint.TextSize * 1.2f;
                var totalTextHeight = lines.Count * lineHeight;

                // Calculate starting Y position to center all lines within element bounds
                var startY = (float)(canvasText.Y + (canvasText.Height - totalTextHeight) / 2) + lineHeight - (lineHeight - paint.TextSize) / 2;

                // Draw each line
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var lineY = startY + (i * lineHeight);

                    // Calculate horizontal position based on alignment
                    float x;
                    switch (canvasText.TextAlign.ToLower())
                    {
                        case "center":
                            x = (float)(canvasText.X + canvasText.Width / 2);
                            paint.TextAlign = SKTextAlign.Center;
                            break;
                        case "right":
                            x = (float)(canvasText.X + canvasText.Width);
                            paint.TextAlign = SKTextAlign.Right;
                            break;
                        default:
                            x = (float)canvasText.X;
                            paint.TextAlign = SKTextAlign.Left;
                            break;
                    }

                    // Draw shadow first if enabled
                    if (canvasText.HasShadow)
                    {
                        using var shadowPaint = new SKPaint();
                        shadowPaint.IsAntialias = true;
                        shadowPaint.TextSize = paint.TextSize;
                        shadowPaint.Typeface = typeface;
                        shadowPaint.TextAlign = paint.TextAlign;

                        // Parse shadow color and apply opacity
                        if (SKColor.TryParse(canvasText.ShadowColor, out var shadowColor))
                        {
                            var alpha = (byte)(shadowColor.Alpha * canvasText.ShadowOpacity);
                            shadowPaint.Color = new SKColor(shadowColor.Red, shadowColor.Green, shadowColor.Blue, alpha);
                        }
                        else
                        {
                            var alpha = (byte)(255 * canvasText.ShadowOpacity);
                            shadowPaint.Color = new SKColor(0, 0, 0, alpha);
                        }

                        // Apply blur effect
                        if (canvasText.ShadowBlur > 0)
                        {
                            shadowPaint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)canvasText.ShadowBlur / 2);
                        }

                        // Draw shadow offset from main text
                        var shadowX = x + (float)canvasText.ShadowOffsetX;
                        var shadowY = lineY + (float)canvasText.ShadowOffsetY;
                        canvas.DrawText(line, shadowX, shadowY, shadowPaint);
                    }

                    // Draw main text
                    canvas.DrawText(line, x, lineY, paint);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to draw text: {Text}", canvasText.Text);
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// The folder a generated canvas image belongs in. Exposed so a caller that stages the
        /// image elsewhere can resolve the same destination when it reveals it.
        /// </summary>
        public static string GetTargetDirectory(CanvasProject project, string baseDirectory)
        {
            var locationPath = Path.Combine(baseDirectory, "MediaAssets", project.LocationId.ToString());
            if (project.ConstraintId.HasValue && project.ConstraintId.Value != 0)
            {
                locationPath = Path.Combine(locationPath, project.ConstraintId.Value.ToString());
            }
            return locationPath;
        }

        /// <param name="overrideDirectory">
        /// Writes the image here instead of its final folder. A caller creating a matching asset row
        /// uses this to keep the image out of the watched location folder until that row exists,
        /// because the manifest builder admits a media file that has no row as legacy content.
        /// </param>
        public async Task<string> SaveCanvasImageAsync(CanvasProject project, string baseDirectory, bool includeBackground = true, string customFileName = "", string? overrideDirectory = null)
        {
            try
            {
                var imageBytes = await GenerateImageAsync(project, includeBackground);

                var locationPath = overrideDirectory ?? GetTargetDirectory(project, baseDirectory);

                Directory.CreateDirectory(locationPath);

                var timestamp = DateTime.Now.ToString("HHmmss");
                var fileName = string.IsNullOrWhiteSpace(customFileName)
                    ? $"Custom_Canvas_{timestamp}.png"
                    : $"{customFileName}_{timestamp}.png";
                var filePath = Path.Combine(locationPath, fileName);

                await File.WriteAllBytesAsync(filePath, imageBytes);

                return fileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving canvas image");
                throw;
            }
        }

        private List<string> WrapText(string text, float maxWidth, SKPaint paint)
        {
            var lines = new List<string>();
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0)
            {
                lines.Add("");
                return lines;
            }

            var currentLine = "";

            foreach (var word in words)
            {
                var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                var testWidth = paint.MeasureText(testLine);

                if (testWidth <= maxWidth)
                {
                    currentLine = testLine;
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentLine))
                    {
                        lines.Add(currentLine);
                        currentLine = word;
                    }
                    else
                    {
                        // Single word is too long, break it character by character
                        var chars = word.ToCharArray();
                        var partialWord = "";

                        foreach (var ch in chars)
                        {
                            var testChar = partialWord + ch;
                            if (paint.MeasureText(testChar) <= maxWidth)
                            {
                                partialWord = testChar;
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(partialWord))
                                {
                                    lines.Add(partialWord);
                                    partialWord = ch.ToString();
                                }
                                else
                                {
                                    // Even single character doesn't fit, add it anyway
                                    lines.Add(ch.ToString());
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(partialWord))
                        {
                            currentLine = partialWord;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
            }

            return lines;
        }

        private async Task DrawImageWithClipping(SKCanvas canvas, CanvasImage canvasImage, CanvasProject project)
        {
            // Calculate the intersection of the image bounds with the canvas bounds
            var imageRect = new SKRect((float)canvasImage.X, (float)canvasImage.Y,
                                       (float)(canvasImage.X + canvasImage.Width),
                                       (float)(canvasImage.Y + canvasImage.Height));
            var canvasRect = new SKRect(0, 0, (float)project.CanvasWidth, (float)project.CanvasHeight);

            if (!imageRect.IntersectsWith(canvasRect))
            {
                // Image is completely outside canvas bounds, don't draw it
                return;
            }

            // Save the current canvas state
            canvas.Save();

            // Clip to canvas bounds
            canvas.ClipRect(canvasRect);

            // Draw the image (it will be automatically clipped)
            await DrawImage(canvas, canvasImage);

            // Restore the canvas state
            canvas.Restore();
        }

        private async Task DrawTextWithClipping(SKCanvas canvas, CanvasText canvasText, CanvasProject project)
        {
            // Calculate the intersection of the text bounds with the canvas bounds
            var textRect = new SKRect((float)canvasText.X, (float)canvasText.Y,
                                      (float)(canvasText.X + canvasText.Width),
                                      (float)(canvasText.Y + canvasText.Height));
            var canvasRect = new SKRect(0, 0, (float)project.CanvasWidth, (float)project.CanvasHeight);

            if (!textRect.IntersectsWith(canvasRect))
            {
                // Text is completely outside canvas bounds, don't draw it
                return;
            }

            // Save the current canvas state
            canvas.Save();

            // Clip to canvas bounds
            canvas.ClipRect(canvasRect);

            // Draw the text (it will be automatically clipped)
            await DrawText(canvas, canvasText);

            // Restore the canvas state
            canvas.Restore();
        }
    }
}