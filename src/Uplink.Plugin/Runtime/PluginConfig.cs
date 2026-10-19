using MyUniverseUplink.Core.Pipeline;
using MyUniverseUplink.Core.Settings;

namespace MyUniverseUplink.Runtime;

/// <summary>
/// Impostazioni effettive per il profilo attivo. Token e valore dell'header extra sono in chiaro solo in memoria:
/// ToString li oscura e non vanno mai scritti nei log.
/// </summary>
public sealed record PluginConfig
{
    public string ProfileId { get; init; } = string.Empty;

    public string ProfileName { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public string ServerUrl { get; init; } = string.Empty;

    public string DeviceId { get; init; } = string.Empty;

    public int BatchSize { get; init; } = 25;

    public int TimeoutSeconds { get; init; } = 30;

    public ThumbnailMode ThumbnailMode { get; init; } = ThumbnailMode.Sampled;

    public int ThumbnailLongEdgePx { get; init; } = 400;

    public int ThumbnailJpegQuality { get; init; } = 70;

    public string? ExtraHeaderName { get; init; }

    public IReadOnlyList<string> ImageTypes { get; init; } = new[] { FrameAdmission.DefaultImageType };

    public bool RequireTarget { get; init; } = true;

    public bool HtmlReport { get; init; }

    /// <summary>Interruttore delle dirette sul sito. Indipendente dall'invio dei frame.</summary>
    public bool StreamsEnabled { get; init; }

    /// <summary>Voci delle dirette, in ordine di lista.</summary>
    public IReadOnlyList<StreamSetting> Streams { get; init; } = Array.Empty<StreamSetting>();

    public StreamsSettings ToStreamsSettings() => new(StreamsEnabled, Streams);

    /// <summary>Segreto: solo in memoria, cifrato su disco con DPAPI.</summary>
    public string? BearerToken { get; init; }

    /// <summary>Segreto: solo in memoria, cifrato su disco con DPAPI.</summary>
    public string? ExtraHeaderValue { get; init; }

    public UplinkSettings ToUplinkSettings() => new()
    {
        Enabled = Enabled,
        ServerUrl = ServerUrl,
        DeviceId = DeviceId,
        BatchSize = BatchSize,
        TimeoutSeconds = TimeoutSeconds,
        ThumbnailMode = ThumbnailMode,
        ThumbnailLongEdgePx = ThumbnailLongEdgePx,
        ThumbnailJpegQuality = ThumbnailJpegQuality,
        ExtraHeaderName = ExtraHeaderName,
        ExtraHeaderValue = ExtraHeaderValue,
    };

    public override string ToString() =>
        $"PluginConfig(profile={ProfileName}, enabled={Enabled}, server={ServerUrl}, token={(string.IsNullOrEmpty(BearerToken) ? "absent" : "present")})";
}
