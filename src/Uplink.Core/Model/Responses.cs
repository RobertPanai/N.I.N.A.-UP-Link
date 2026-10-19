namespace MyUniverseUplink.Core.Model;

/// <summary>Risposta di POST /api/uplink/frames (HTTP 200).</summary>
public sealed class BatchResponse
{
    public int SchemaVersion { get; init; }

    public string BatchId { get; init; } = string.Empty;

    public DateTimeOffset? ServerTimeUtc { get; init; }

    public List<FrameResult> Results { get; init; } = new();

    public int Accepted { get; init; }

    public int Duplicates { get; init; }

    public int Rejected { get; init; }
}

public sealed class FrameResult
{
    public string FrameId { get; init; } = string.Empty;

    /// <summary>Uno tra FrameStatus.Stored, Duplicate, Rejected.</summary>
    public string Status { get; init; } = string.Empty;

    public string? Reason { get; init; }
}

public static class FrameStatus
{
    public const string Stored = "stored";
    public const string Duplicate = "duplicate";
    public const string Rejected = "rejected";
}

/// <summary>Risposta di GET /api/uplink/health. Tutti i campi tranne lo schema sono opzionali (tolerant reader).</summary>
public sealed class HealthResponse
{
    public int SchemaVersion { get; init; }

    public string? ServerVersion { get; init; }

    public int? MaxFramesPerBatch { get; init; }

    public long? MaxBodyBytes { get; init; }

    public bool? AcceptsThumbnails { get; init; }

    public int? MaxThumbnailBytes { get; init; }

    public bool? Authenticated { get; init; }

    public string? DeviceId { get; init; }

    public DateTimeOffset? ServerTimeUtc { get; init; }
}
