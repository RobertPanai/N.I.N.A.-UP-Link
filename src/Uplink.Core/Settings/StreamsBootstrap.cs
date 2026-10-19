namespace MyUniverseUplink.Core.Settings;

/// <summary>Voce di bootstrap.json. Enabled e' facoltativo nel file (default true).</summary>
public sealed record BootstrapStream(string Title, string? Description, string Url, bool Enabled);

public static class StreamsBootstrap
{
    /// <summary>Unisce le voci di bootstrap all'insieme attuale: una voce con lo stesso titolo riprende il suo StreamId.</summary>
    public static IReadOnlyList<StreamSetting> Merge(IReadOnlyList<StreamSetting> current, IReadOnlyList<BootstrapStream> incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);

        var taken = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<StreamSetting>(incoming.Count);
        foreach (var entry in incoming)
        {
            var title = entry.Title.Trim();
            var previous = current.FirstOrDefault(c =>
                string.Equals(c.Title.Trim(), title, StringComparison.Ordinal) && !taken.Contains(c.StreamId));
            var id = previous?.StreamId ?? StreamIds.Create(title, taken);
            taken.Add(id);

            var description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim();
            result.Add(new StreamSetting(id, title, description, entry.Url.Trim(), entry.Enabled));
        }

        return result;
    }
}
