using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyUniverseUplink.Core.Settings;

/// <summary>JSON compatto delle voci nelle impostazioni del profilo. Non contiene segreti.</summary>
public static class StreamsSettingsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Serialize(IEnumerable<StreamSetting> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var entries = items.Select(i => new Entry
        {
            StreamId = i.StreamId,
            Title = i.Title,
            Description = i.Description,
            Url = i.Url,
            Enabled = i.Enabled,
        }).ToList();
        return JsonSerializer.Serialize(entries, Options);
    }

    /// <summary>False se il testo non e' un elenco JSON leggibile (in quel caso items resta vuoto).</summary>
    public static bool TryParse(string? json, out IReadOnlyList<StreamSetting> items)
    {
        items = Array.Empty<StreamSetting>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<Entry?>>(json, Options);
            if (entries is null)
            {
                return false;
            }

            items = entries
                .Where(e => e is not null)
                .Select(e => new StreamSetting(
                    e!.StreamId ?? string.Empty,
                    e.Title ?? string.Empty,
                    e.Description,
                    e.Url ?? string.Empty,
                    e.Enabled ?? true))
                .ToList();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class Entry
    {
        public string? StreamId { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? Url { get; set; }

        public bool? Enabled { get; set; }
    }
}
