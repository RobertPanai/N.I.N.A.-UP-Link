namespace MyUniverseUplink.Core.Settings;

/// <summary>Impostazioni del plugin. Il token NON sta qui: viene passato al client come segreto separato.</summary>
public sealed class UplinkSettings
{
    public bool Enabled { get; set; } = true;

    public string ServerUrl { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public int BatchSize { get; set; } = 25;

    public int BatchIntervalSeconds { get; set; } = 20;

    public int TimeoutSeconds { get; set; } = 30;

    public ThumbnailMode ThumbnailMode { get; set; } = ThumbnailMode.Sampled;

    public int ThumbnailLongEdgePx { get; set; } = 400;

    public int ThumbnailJpegQuality { get; set; } = 70;

    /// <summary>Header extra opzionale, es. x-vercel-protection-bypass. Il valore e' un segreto: mai in log.</summary>
    public string? ExtraHeaderName { get; set; }

    /// <summary>Valore del header extra. Mai in log, mai in messaggi di errore.</summary>
    public string? ExtraHeaderValue { get; set; }
}

public enum ThumbnailMode
{
    Always,
    Sampled,
    Never,
}
