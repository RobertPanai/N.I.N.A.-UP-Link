using System.Text.Json.Serialization;

namespace MyUniverseUplink.Core.Model;

/// <summary>Un frame nel payload. Tutto tranne FrameId, ImageType e CapturedAtUtc e' opzionale (tolerant reader).</summary>
public sealed class FrameRecord
{
    public string FrameId { get; init; } = string.Empty;

    public string ImageType { get; init; } = "LIGHT";

    public DateTimeOffset? CapturedAtUtc { get; init; }

    public DateTimeOffset? SavedAtUtc { get; init; }

    public string? NightId { get; init; }

    public string? SequenceTitle { get; init; }

    public TargetInfo? Target { get; init; }

    public EquipmentInfo? Equipment { get; init; }

    public ExposureInfo? Exposure { get; init; }

    public QualityInfo? Quality { get; init; }

    [JsonPropertyName("environment")]
    public ConditionsInfo? Conditions { get; init; }

    public SourceInfo? Source { get; init; }

    public ThumbnailInfo? Thumbnail { get; init; }
}

public sealed class TargetInfo
{
    public string? Name { get; init; }

    public double? RaHours { get; init; }

    public double? DecDegrees { get; init; }

    /// <summary>Derivato, solo per continuita' con integration.csv.</summary>
    public string? RaText { get; init; }

    /// <summary>Derivato, solo per continuita' con integration.csv.</summary>
    public string? DecText { get; init; }

    public double? RotationDegrees { get; init; }
}

public sealed class EquipmentInfo
{
    public string? CameraName { get; init; }

    public double? PixelSizeUm { get; init; }

    public string? TelescopeName { get; init; }

    public double? FocalLengthMm { get; init; }

    public double? FocalRatio { get; init; }

    public string? MountName { get; init; }

    public double? ImageScaleArcsecPerPx { get; init; }
}

public sealed class ExposureInfo
{
    public string? Filter { get; init; }

    public double? DurationSeconds { get; init; }

    public int? Gain { get; init; }

    public int? Offset { get; init; }

    public string? Binning { get; init; }

    public double? SensorTemperatureC { get; init; }
}

public sealed class QualityInfo
{
    public double? Hfr { get; init; }

    public double? HfrStDev { get; init; }

    public int? StarCount { get; init; }

    public double? Mean { get; init; }

    public double? Median { get; init; }

    public double? StDev { get; init; }

    public double? RmsTotalArcsec { get; init; }

    public double? RmsRaArcsec { get; init; }

    public double? RmsDecArcsec { get; init; }

    public double? AltitudeDeg { get; init; }

    public double? AzimuthDeg { get; init; }

    public double? Airmass { get; init; }
}

public sealed class ConditionsInfo
{
    public double? SkyQualityMpsas { get; init; }

    public double? TemperatureC { get; init; }

    public double? HumidityPercent { get; init; }

    public double? CloudCoverPercent { get; init; }
}

/// <summary>Solo il nome file: mai il percorso assoluto.</summary>
public sealed class SourceInfo
{
    public string? FileName { get; init; }
}

public sealed class ThumbnailInfo
{
    public string? Format { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public int? ByteLength { get; init; }

    public string? Sha256 { get; init; }

    public string? Base64 { get; init; }
}
