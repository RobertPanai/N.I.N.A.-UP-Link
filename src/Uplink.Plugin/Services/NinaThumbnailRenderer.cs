using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Pipeline;

namespace MyUniverseUplink.Services;

/// <summary>
/// BitmapSource -> miniatura JPEG in base64. Va chiamato fuori dal thread di ImageSaved.
/// Media a blocchi fino al lato lungo richiesto. Stretch percentile 0.5-99.5 con gamma 2.2 solo per immagini
/// a 16 bit o molto scure (mediana sotto 6%). Tetto 200 KB: se il JPEG e' piu' grande abbassa la qualita' fino a 40.
/// Per immagini a 16 bit legge l'intero bitmap in memoria (transitorio).
/// </summary>
public sealed class NinaThumbnailRenderer
{
    public const int MaxBytes = 200_000;

    private const int MinQuality = 40;
    private const int HistogramBins = 4096;
    private const double DarkMedianThreshold = 0.06;
    private const double Gamma = 2.2;

    /// <returns>La miniatura, o null se non rientra nel tetto di dimensione.</returns>
    public ThumbnailInfo? Render(BitmapSource source, int longEdgePx, int jpegQuality)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.CanFreeze && !source.IsFrozen)
        {
            source.Freeze();
        }

        var sw = source.PixelWidth;
        var sh = source.PixelHeight;
        var (tw, th) = ThumbnailSizing.Fit(sw, sh, longEdgePx);

        var sixteenBit = source.Format == PixelFormats.Gray16 || source.Format == PixelFormats.Rgb48;
        int channels;
        float[] average;

        if (source.Format == PixelFormats.Gray16)
        {
            var raw = new ushort[sw * sh];
            source.CopyPixels(raw, sw * 2, 0);
            channels = 1;
            average = AverageBlocks(sw, sh, tw, th, 1, (x, y, _) => raw[y * sw + x] / 65535f);
        }
        else if (source.Format == PixelFormats.Rgb48)
        {
            var raw = new ushort[sw * sh * 3];
            source.CopyPixels(raw, sw * 6, 0);
            channels = 3;
            average = AverageBlocks(sw, sh, tw, th, 3, (x, y, c) => raw[(y * sw + x) * 3 + c] / 65535f);
        }
        else
        {
            BitmapSource converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var raw = new byte[sw * sh * 4];
            converted.CopyPixels(raw, sw * 4, 0);
            channels = 3;
            // Canali in ordine R,G,B; in Bgra il byte 0 e' B, il 2 e' R.
            average = AverageBlocks(sw, sh, tw, th, 3, (x, y, c) => raw[(y * sw + x) * 4 + (2 - c)] / 255f);
        }

        var histogram = new long[HistogramBins];
        var pixelCount = tw * th;
        for (var i = 0; i < pixelCount; i++)
        {
            var luma = channels == 1 ? average[i] : (average[i * 3] + average[i * 3 + 1] + average[i * 3 + 2]) / 3f;
            histogram[Math.Min(HistogramBins - 1, (int)(luma * HistogramBins))]++;
        }

        var (medianBin, _) = StretchMath.PercentileBins(histogram, 50, 50);
        var median = (medianBin + 0.5) / HistogramBins;
        var stretch = sixteenBit || median < DarkMedianThreshold;
        double low = 0;
        double high = 1;
        if (stretch)
        {
            var (lowBin, highBin) = StretchMath.PercentileBins(histogram, 0.5, 99.5);
            low = lowBin / (double)HistogramBins;
            high = (highBin + 1) / (double)HistogramBins;
        }

        var bgr = new byte[pixelCount * 4];
        for (var i = 0; i < pixelCount; i++)
        {
            double r;
            double g;
            double b;
            if (channels == 1)
            {
                r = g = b = average[i];
            }
            else
            {
                r = average[i * 3];
                g = average[i * 3 + 1];
                b = average[i * 3 + 2];
            }

            if (stretch)
            {
                r = StretchMath.Map(r, low, high, Gamma);
                g = StretchMath.Map(g, low, high, Gamma);
                b = StretchMath.Map(b, low, high, Gamma);
            }

            var o = i * 4;
            bgr[o] = ToByte(b);
            bgr[o + 1] = ToByte(g);
            bgr[o + 2] = ToByte(r);
            bgr[o + 3] = 255;
        }

        var bitmap = BitmapSource.Create(tw, th, 96, 96, PixelFormats.Bgr32, null, bgr, tw * 4);
        bitmap.Freeze();

        var quality = Math.Clamp(jpegQuality, MinQuality, 95);
        var jpeg = EncodeJpeg(bitmap, quality);
        while (jpeg.Length > MaxBytes && quality > MinQuality)
        {
            quality = Math.Max(MinQuality, quality - 10);
            jpeg = EncodeJpeg(bitmap, quality);
        }

        if (jpeg.Length > MaxBytes)
        {
            return null;
        }

        return new ThumbnailInfo
        {
            Format = "jpeg",
            Width = tw,
            Height = th,
            ByteLength = jpeg.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(jpeg)).ToLowerInvariant(),
            Base64 = Convert.ToBase64String(jpeg),
        };
    }

    /// <summary>Media dei valori normalizzati (0-1) su ogni blocco sorgente. Output interleaved per canale.</summary>
    private static float[] AverageBlocks(int sw, int sh, int tw, int th, int channels, Func<int, int, int, float> sample)
    {
        var result = new float[tw * th * channels];
        for (var ty = 0; ty < th; ty++)
        {
            var y0 = ty * sh / th;
            var y1 = Math.Max(y0 + 1, (ty + 1) * sh / th);
            for (var tx = 0; tx < tw; tx++)
            {
                var x0 = tx * sw / tw;
                var x1 = Math.Max(x0 + 1, (tx + 1) * sw / tw);
                for (var c = 0; c < channels; c++)
                {
                    double sum = 0;
                    var count = 0;
                    for (var y = y0; y < y1 && y < sh; y++)
                    {
                        for (var x = x0; x < x1 && x < sw; x++)
                        {
                            sum += sample(x, y, c);
                            count++;
                        }
                    }

                    result[(ty * tw + tx) * channels + c] = count == 0 ? 0f : (float)(sum / count);
                }
            }
        }

        return result;
    }

    private static byte[] EncodeJpeg(BitmapSource bitmap, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
