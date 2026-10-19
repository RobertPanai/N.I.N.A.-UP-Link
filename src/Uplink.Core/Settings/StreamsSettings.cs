namespace MyUniverseUplink.Core.Settings;

/// <summary>
/// Una diretta configurata. StreamId e' stabile (lo genera il plugin la prima volta, vedi StreamIds).
/// Le voci con Enabled=false restano nelle impostazioni ma non vengono inviate.
/// </summary>
public sealed record StreamSetting(string StreamId, string Title, string? Description, string Url, bool Enabled);

/// <summary>Insieme delle dirette: interruttore e lista ordinata (l'ordine e' la posizione nella lista).</summary>
public sealed record StreamsSettings(bool Enabled, IReadOnlyList<StreamSetting> Items)
{
    public static StreamsSettings Off { get; } = new(false, Array.Empty<StreamSetting>());
}

/// <summary>Limiti del contratto streams-v1 (max 12 voci, titolo 80, descrizione 400, URL 2000).</summary>
public static class StreamsLimits
{
    public const int MaxEntries = 12;
    public const int MaxTitleLength = 80;
    public const int MaxDescriptionLength = 400;
    public const int MaxUrlLength = 2000;
}
