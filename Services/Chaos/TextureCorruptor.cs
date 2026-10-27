using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class TextureCorruptor
{
    private readonly Random _random;
    public TextureCorruptor(Random random) { _random = random; }
        internal void CorruptTexturePixels(string filePath, double intensity, int textureMode)
        {
            try
            {
                byte[] pixels;
                int width, height, stride;
                double DpiX, DpiY;

                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    width = frame.PixelWidth;
                    height = frame.PixelHeight;
                    DpiX = frame.DpiX;
                    DpiY = frame.DpiY;

                    // Convert to Bgra32 for uniform byte manipulation
                    var formatted = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    stride = width * 4;
                    pixels = new byte[height * stride];
                    formatted.CopyPixels(pixels, stride, 0);
                }

                double rate = (intensity / 500.0); // scale rate to 0.0 - 1.0 based on new 500% limit

                switch (textureMode)
                {
                    case 1: // Color Inversion (Negative)
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            if (_random.NextDouble() < rate)
                            {
                                pixels[i] = (byte)(255 - pixels[i]);       // Blue
                                pixels[i + 1] = (byte)(255 - pixels[i + 1]); // Green
                                pixels[i + 2] = (byte)(255 - pixels[i + 2]); // Red
                            }
                        }
                        break;

                    case 2: // Hue Shift (Color Cycle)
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            if (_random.NextDouble() < rate)
                            {
                                byte b = pixels[i];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                pixels[i] = g;
                                pixels[i + 1] = r;
                                pixels[i + 2] = b;
                            }
                        }
                        break;

                    case 3: // Grayscale & Desaturate
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            if (_random.NextDouble() < rate)
                            {
                                byte b = pixels[i];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                byte gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                                pixels[i] = gray;
                                pixels[i + 1] = gray;
                                pixels[i + 2] = gray;
                            }
                        }
                        break;

                    case 4: // Noise Injection
                        int noiseRange = (int)(rate * 100);
                        if (noiseRange < 5) noiseRange = 5;
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            if (_random.NextDouble() < rate)
                            {
                                int val = _random.Next(-noiseRange, noiseRange + 1);
                                pixels[i] = (byte)Math.Clamp(pixels[i] + val, 0, 255);
                                pixels[i + 1] = (byte)Math.Clamp(pixels[i + 1] + val, 0, 255);
                                pixels[i + 2] = (byte)Math.Clamp(pixels[i + 2] + val, 0, 255);
                            }
                        }
                        break;

                    case 5: // Pixelate / Mosaic
                        int blockSize = (int)(rate * 16);
                        if (blockSize < 2) blockSize = 2;
                        for (int y = 0; y < height; y += blockSize)
                        {
                            for (int x = 0; x < width; x += blockSize)
                            {
                                int cx = Math.Min(x + blockSize / 2, width - 1);
                                int cy = Math.Min(y + blockSize / 2, height - 1);
                                int centerIdx = (cy * width + cx) * 4;
                                byte b = pixels[centerIdx];
                                byte g = pixels[centerIdx + 1];
                                byte r = pixels[centerIdx + 2];
                                byte a = pixels[centerIdx + 3];

                                for (int by = 0; by < blockSize && y + by < height; by++)
                                {
                                    for (int bx = 0; bx < blockSize && x + bx < width; bx++)
                                    {
                                        int idx = ((y + by) * width + (x + bx)) * 4;
                                        pixels[idx] = b;
                                        pixels[idx + 1] = g;
                                        pixels[idx + 2] = r;
                                        pixels[idx + 3] = a;
                                    }
                                }
                            }
                        }
                        break;


                }

                // Write back as PNG
                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                    width, height, DpiX, DpiY,
                    System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using (var outStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(outStream);
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Error corrupting texture {filePath}: {ex.Message}", ex);
            }
        }
        internal byte[] GetTextureXPixels(string customPath, int targetWidth, int targetHeight)
        {
            byte[] targetPixels = new byte[targetWidth * targetHeight * 4];
            System.Windows.Media.Imaging.BitmapSource src = null;

            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            {
                try
                {
                    using (var stream = new FileStream(customPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        src = decoder.Frames[0];
                    }
                }
                catch (Exception ex)
                {
                    throw new IOException($"Error reading custom texture file: {ex.Message}", ex);
                }
            }

            if (src == null)
            {
                // Generate default "X" image of size targetWidth x targetHeight
                for (int y = 0; y < targetHeight; y++)
                {
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int idx = (y * targetWidth + x) * 4;
                        double normX = (double)x / targetWidth;
                        double normY = (double)y / targetHeight;
                        bool onDiagonal1 = Math.Abs(normX - normY) < 0.05;
                        bool onDiagonal2 = Math.Abs(normX - (1.0 - normY)) < 0.05;
                        if (onDiagonal1 || onDiagonal2)
                        {
                            targetPixels[idx] = 0;
                            targetPixels[idx + 1] = 0;
                            targetPixels[idx + 2] = 255;
                            targetPixels[idx + 3] = 255;
                        }
                        else
                        {
                            targetPixels[idx] = 0;
                            targetPixels[idx + 1] = 0;
                            targetPixels[idx + 2] = 0;
                            targetPixels[idx + 3] = 255;
                        }
                    }
                }
                return targetPixels;
            }

            double scaleX = (double)targetWidth / src.PixelWidth;
            double scaleY = (double)targetHeight / src.PixelHeight;

            var scaleTransform = new System.Windows.Media.ScaleTransform(scaleX, scaleY);
            var resized = new System.Windows.Media.Imaging.TransformedBitmap(src, scaleTransform);
            var formatted = new System.Windows.Media.Imaging.FormatConvertedBitmap(resized, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            
            formatted.CopyPixels(targetPixels, targetWidth * 4, 0);
            return targetPixels;
        }
}
