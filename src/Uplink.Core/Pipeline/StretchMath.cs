namespace MyUniverseUplink.Core.Pipeline;

/// <summary>Stretch automatico per miniature di immagini lineari o scure: percentili su istogramma e gamma.</summary>
public static class StretchMath
{
    /// <summary>
    /// Indici di bin ai percentili indicati (0-100) sull'istogramma cumulato. Istogramma vuoto: (0, ultimo bin).
    /// </summary>
    public static (int Low, int High) PercentileBins(ReadOnlySpan<long> histogram, double lowPercent, double highPercent)
    {
        if (histogram.Length == 0)
        {
            throw new ArgumentException("L'istogramma non puo' essere vuoto.", nameof(histogram));
        }

        if (lowPercent < 0 || highPercent > 100 || lowPercent > highPercent)
        {
            throw new ArgumentOutOfRangeException(nameof(lowPercent), "Percentili non validi.");
        }

        long total = 0;
        foreach (var count in histogram)
        {
            total += count;
        }

        if (total == 0)
        {
            return (0, histogram.Length - 1);
        }

        var low = BinAtCumulative(histogram, total * lowPercent / 100.0);
        var high = BinAtCumulative(histogram, total * highPercent / 100.0);
        return (low, Math.Max(low, high));
    }

    /// <summary>Mappa un valore nell'intervallo [low, high] su [0, 1] con gamma (out = x^(1/gamma)). Fuori intervallo: clamp.</summary>
    public static double Map(double value, double low, double high, double gamma)
    {
        if (gamma <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gamma));
        }

        if (high <= low)
        {
            return value >= high ? 1.0 : 0.0;
        }

        var x = Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        return Math.Pow(x, 1.0 / gamma);
    }

    private static int BinAtCumulative(ReadOnlySpan<long> histogram, double target)
    {
        long running = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            running += histogram[i];
            if (running >= target)
            {
                return i;
            }
        }

        return histogram.Length - 1;
    }
}
