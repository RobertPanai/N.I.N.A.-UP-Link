namespace MyUniverseUplink.Core.Model;

/// <summary>
/// Richiesta di PUT /api/uplink/streams (contratto v1, fonte di verita': contract/streams-v1.request.json).
/// Sostituisce l'intero insieme di dirette del dispositivo: lista vuota o streamsEnabled=false cancella tutto.
/// </summary>
public sealed class StreamsRequest
{
    public int SchemaVersion { get; init; } = UplinkInfo.ContractSchemaVersion;

    public string RequestId { get; init; } = string.Empty;

    public DeviceInfo Device { get; init; } = new();

    public bool StreamsEnabled { get; init; }

    public List<StreamEntry> Streams { get; init; } = new();
}

/// <summary>Una diretta nella richiesta. Description omessa quando non c'e'; Order e' la posizione nella lista.</summary>
public sealed class StreamEntry
{
    public string StreamId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string Url { get; init; } = string.Empty;

    public int Order { get; init; }
}

/// <summary>Risposta di PUT /api/uplink/streams (HTTP 200).</summary>
public sealed class StreamsResponse
{
    public int SchemaVersion { get; init; }

    public string? RequestId { get; init; }

    public DateTimeOffset? ServerTimeUtc { get; init; }

    public List<StreamResult> Results { get; init; } = new();

    public int Stored { get; init; }

    public int Rejected { get; init; }

    public int Removed { get; init; }

    public bool? Visible { get; init; }
}

/// <summary>Esito per singola voce: Status vale StreamResultStatus.Stored o Rejected, con Reason quando rifiutata.</summary>
public sealed class StreamResult
{
    public string StreamId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? Reason { get; init; }
}

public static class StreamResultStatus
{
    public const string Stored = "stored";
    public const string Rejected = "rejected";
}
