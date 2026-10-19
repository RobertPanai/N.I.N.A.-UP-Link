namespace MyUniverseUplink.Core.Model;

/// <summary>Richiesta di POST /api/uplink/frames (contratto v1, fonte di verità: contract/batch-v1.request.json).</summary>
public sealed class FrameBatch
{
    public int SchemaVersion { get; init; } = UplinkInfo.ContractSchemaVersion;

    public string BatchId { get; init; } = string.Empty;

    public DeviceInfo Device { get; init; } = new();

    public List<FrameRecord> Frames { get; init; } = new();
}

/// <summary>Identità del dispositivo che invia. Nessuna coordinata dell'osservatorio, nessun percorso assoluto.</summary>
public sealed class DeviceInfo
{
    public string DeviceId { get; init; } = string.Empty;

    public string? HostName { get; init; }

    public string? PluginVersion { get; init; }

    public string? NinaVersion { get; init; }

    public string? ProfileId { get; init; }

    public string? ProfileName { get; init; }
}
