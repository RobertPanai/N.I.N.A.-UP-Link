using System.IO;
using MyUniverseUplink.Core.Identity;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Pipeline;
using NINA.WPF.Base.Interfaces.Mediator;

namespace MyUniverseUplink.Services;

/// <summary>Dati del profilo e regole di ammissione per il mapping di un frame.</summary>
public sealed record MappingContext(string ProfileId, IReadOnlyCollection<string> AllowedImageTypes, bool RequireTarget);

/// <summary>
/// Traduce ImageSavedEventArgs di NINA in FrameRecord del Core. Solo metadati: nessun percorso assoluto,
/// nessuna coordinata dell'osservatorio. Non lancia per dati mancanti: restituisce false con il motivo.
/// Le date di NINA senza kind vengono trattate come ora locale.
/// </summary>
public static class NinaFrameMapper
{
    public static bool TryMap(
        ImageSavedEventArgs args,
        MappingContext context,
        DateTimeOffset savedAtUtc,
        MyUniverseUplink.Core.Model.ThumbnailInfo? thumbnail,
        out FrameRecord? frame,
        out string reason)
    {
        frame = null;
        var meta = args.MetaData;
        var image = meta?.Image;
        if (meta is null || image is null)
        {
            reason = "no image metadata";
            return false;
        }

        var targetName = Blank(meta.Target?.Name);
        var admission = FrameAdmission.Evaluate(image.ImageType, targetName is not null, context.AllowedImageTypes, context.RequireTarget);
        if (!admission.Accepted)
        {
            reason = admission.Reason;
            return false;
        }

        var localPath = args.PathToImage?.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            reason = "no file path";
            return false;
        }

        if (image.ExposureStart == default)
        {
            reason = "no exposure start";
            return false;
        }

        var fileName = Path.GetFileName(localPath);
        var capturedUtc = ToUtc(image.ExposureStart);
        var camera = meta.Camera;
        var telescope = meta.Telescope;
        var cameraName = Blank(camera?.Name) ?? string.Empty;
        var frameId = FrameIdFactory.Create(context.ProfileId, cameraName, capturedUtc, fileName);

        TargetInfo? target = null;
        if (targetName is not null)
        {
            var coords = meta.Target?.Coordinates;
            // Coordinate tutte a zero = non impostate in NINA: non vanno inviate come dato reale.
            var hasCoords = coords is not null && (coords.RADegrees != 0 || coords.Dec != 0);
            target = new TargetInfo
            {
                Name = targetName,
                RaHours = hasCoords ? Finite(coords!.RADegrees / 15.0) : null,
                DecDegrees = hasCoords ? Finite(coords!.Dec) : null,
                RotationDegrees = Finite(meta.Target?.PositionAngle),
            };
        }

        var pixelSize = Positive(camera?.PixelSize);
        var focalLength = Positive(telescope?.FocalLength);
        var equipment = new EquipmentInfo
        {
            CameraName = Blank(camera?.Name),
            PixelSizeUm = pixelSize,
            TelescopeName = Blank(telescope?.Name),
            FocalLengthMm = focalLength,
            FocalRatio = Positive(telescope?.FocalRatio),
            // 206.265 * pixel(um) / focale(mm) = arcsec per pixel.
            ImageScaleArcsecPerPx = pixelSize is { } p && focalLength is { } f ? 206.265 * p / f : null,
        };

        var exposure = new ExposureInfo
        {
            Filter = Blank(args.Filter) ?? Blank(meta.FilterWheel?.Filter),
            DurationSeconds = Positive(image.ExposureTime),
            Gain = camera is not null && camera.Gain >= 0 ? camera.Gain : null,
            Offset = camera is not null && camera.Offset >= 0 ? camera.Offset : null,
            Binning = camera is not null ? camera.BinX + "x" + camera.BinY : null,
            SensorTemperatureC = Finite(camera?.Temperature),
        };

        var stars = args.StarDetectionAnalysis;
        var stats = args.Statistics;
        var quality = new QualityInfo
        {
            Hfr = Positive(stars?.HFR),
            HfrStDev = Finite(stars?.HFRStDev),
            StarCount = stars?.DetectedStars,
            Mean = Finite(stats?.Mean),
            Median = Finite(stats?.Median),
            StDev = Finite(stats?.StDev),
            AltitudeDeg = Finite(telescope?.Altitude),
            AzimuthDeg = Finite(telescope?.Azimuth),
            Airmass = Positive(telescope?.Airmass),
        };

        var weather = meta.WeatherData;
        var conditions = weather is null ? null : new ConditionsInfo
        {
            SkyQualityMpsas = Finite(weather.SkyQuality),
            TemperatureC = Finite(weather.Temperature),
            HumidityPercent = Finite(weather.Humidity),
            CloudCoverPercent = Finite(weather.CloudCover),
        };

        frame = new FrameRecord
        {
            FrameId = frameId,
            ImageType = image.ImageType!.Trim().ToUpperInvariant(),
            CapturedAtUtc = capturedUtc,
            SavedAtUtc = savedAtUtc,
            SequenceTitle = Blank(meta.Sequence?.Title),
            Target = target,
            Equipment = equipment,
            Exposure = exposure,
            Quality = quality,
            Conditions = conditions,
            Source = new SourceInfo { FileName = fileName },
            Thumbnail = thumbnail,
        };

        reason = "accepted";
        return true;
    }

    internal static DateTimeOffset ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(value, TimeSpan.Zero)
            : new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    private static double? Positive(double? value) => value is { } v && double.IsFinite(v) && v > 0 ? v : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
