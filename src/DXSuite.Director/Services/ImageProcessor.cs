using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DXSuite.Director.Services
{
    public class ImageProcessor
    {
        public static byte[] InvertAlphaToGrayscaleMask(byte[] originalBytes)
        {
            using var ms = new MemoryStream(originalBytes);
            using var bitmap = new Bitmap(ms);

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    byte alpha = pixel.A;

                    var gray = Color.FromArgb(255, alpha, alpha, alpha);
                    bitmap.SetPixel(x, y, gray);
                }
            }

            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
    }
}
