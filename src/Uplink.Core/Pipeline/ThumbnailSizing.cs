namespace MyUniverseUplink.Core.Pipeline;

/// <summary>Dimensioni della miniatura: lato lungo fissato, proporzioni mantenute, mai ingrandimenti.</summary>
public static class ThumbnailSizing
{
    public static (int Width, int Height) Fit(int sourceWidth, int sourceHeight, int longEdgePx)
    {
        if (sourceWidth < 1 || sourceHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Le dimensioni sorgente devono essere positive.");
        }

        if (longEdgePx < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(longEdgePx));
        }

        var longSide = Math.Max(sourceWidth, sourceHeight);
        if (longSide <= longEdgePx)
        {
            return (sourceWidth, sourceHeight);
        }

        var scale = (double)longEdgePx / longSide;
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero));
        return (width, height);
    }
}
