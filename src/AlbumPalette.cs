using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IslandPanel;
public static class AlbumPalette
{
    public static Color Default => Color.FromRgb(232, 98, 153);
    public static Color Extract(BitmapSource? image)
    {
        if (image == null) return Default;
        try
        {
            var b = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); int stride = b.PixelWidth * 4; byte[] pixels = new byte[stride * b.PixelHeight]; b.CopyPixels(pixels, stride, 0);
            double[] weights = new double[12], reds = new double[12], greens = new double[12], blues = new double[12];
            for (int y = 0; y < b.PixelHeight; y += Math.Max(1, b.PixelHeight / 24)) for (int x = 0; x < b.PixelWidth; x += Math.Max(1, b.PixelWidth / 24))
            {
                int p = y * stride + x * 4; double r = pixels[p + 2] / 255d, g = pixels[p + 1] / 255d, blue = pixels[p] / 255d;
                double max = Math.Max(r, Math.Max(g, blue)), min = Math.Min(r, Math.Min(g, blue)), delta = max - min; if (max < .14 || delta < .12) continue;
                double hue = max == r ? ((g - blue) / delta + 6) % 6 : max == g ? (blue - r) / delta + 2 : (r - g) / delta + 4;
                int bucket = Math.Clamp((int)(hue * 2), 0, 11); double weight = delta * delta * max; weights[bucket] += weight; reds[bucket] += r * weight; greens[bucket] += g * weight; blues[bucket] += blue * weight;
            }
            int best = 0; for (int i = 1; i < 12; i++) if (weights[i] > weights[best]) best = i; if (weights[best] == 0) return Default;
            double rr = reds[best] / weights[best], gg = greens[best] / weights[best], bb = blues[best] / weights[best], gain = .92 / Math.Max(rr, Math.Max(gg, bb));
            return Color.FromRgb((byte)Math.Clamp(rr * gain * 255 + 12, 0, 255), (byte)Math.Clamp(gg * gain * 255 + 12, 0, 255), (byte)Math.Clamp(bb * gain * 255 + 12, 0, 255));
        }
        catch { return Default; }
    }
    public static Brush Background(Color c)
    {
        var b = new LinearGradientBrush(new GradientStopCollection { new(Color.FromRgb((byte)(c.R * .21), (byte)(c.G * .15), (byte)(c.B * .19)), 0), new(Color.FromRgb(18, 17, 20), .65), new(Color.FromRgb(15, 15, 17), 1) }, 0); b.Freeze(); return b;
    }
}
