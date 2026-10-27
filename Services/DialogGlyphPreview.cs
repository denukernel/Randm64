using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sm64DecompLevelViewer.Services;

public sealed class DialogGlyphPreview : FrameworkElement
{
    private readonly Dictionary<char, BitmapSource> _glyphs = new();
    private const string Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private static readonly int[] Widths = { 7,7,7,7,7,7,7,7,7,7,6,6,6,6,6,6,6,6,5,6,6,5,8,8,6,6,6,6,6,5,6,6,8,7,6,6,6,5,5,6,5,5,6,5,4,5,5,3,7,5,5,5,6,5,5,5,5,5,7,7,5,5 };
    public string Text { get; set; } = "";
    public bool HasGameFont => _glyphs.Count == Characters.Length;
    public DialogGlyphPreview(string root)
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        for (int i = 0; i < Characters.Length; i++)
        {
            string path = Path.Combine(root, "textures", "segment2", $"font_graphics.{0x5900 + i * 0x40:X5}.ia4.png");
            // Extracted asset filenames use lower-case hexadecimal in some trees.
            if (!File.Exists(path)) path = Path.Combine(root, "textures", "segment2", $"font_graphics.{0x5900 + i * 0x40:x5}.ia4.png");
            try
            {
                if (!File.Exists(path)) continue;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
                // The game texture rectangle swaps the texture axes for these 16x8 assets.
                if (image.PixelWidth == 16 && image.PixelHeight == 8)
                {
                    var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                    byte[] data = new byte[128 * 4];
                    pixels.CopyPixels(data, 16 * 4, 0);
                    byte[] transposed = new byte[data.Length];
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 8; x++)
                            Array.Copy(data, ((7 - x) * 16 + (15 - y)) * 4, transposed, (y * 8 + x) * 4, 4);
                    var glyph = BitmapSource.Create(8, 16, 96, 96, PixelFormats.Bgra32, null, transposed, 8 * 4);
                    glyph.Freeze(); _glyphs[Characters[i]] = glyph;
                }
                else if (image.PixelWidth == 8 && image.PixelHeight == 16) _glyphs[Characters[i]] = image;
            }
            catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UnauthorizedAccessException) { }
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        double x = 0, y = 0;
        foreach (char c in Text)
        {
            if (c == '\n') { x = 0; y += 16; continue; }
            if (c == ' ') { x += 5; continue; }
            int index = Characters.IndexOf(c);
            if (_glyphs.TryGetValue(c, out var image)) dc.DrawImage(image, new Rect(x, y, 8, 16));
            else dc.DrawText(new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 10, Brushes.White, 1), new Point(x, y));
            x += index >= 0 ? Widths[index] : 5;
        }
    }
}



