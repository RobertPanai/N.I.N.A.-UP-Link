using System.Text.RegularExpressions;

namespace MyUniverseUplink.Core.Settings;

/// <summary>Problema di validazione: Index e' la posizione della voce, oppure StreamsValidator.ListLevel per l'insieme.</summary>
public sealed record StreamIssue(int Index, string Message);

/// <summary>
/// Regole delle dirette: titolo 1-80, descrizione fino a 400, URL https senza credenziali fino a 2000,
/// al massimo 12 voci, StreamId valido e unico. I messaggi non ripetono mai l'URL.
/// </summary>
public static partial class StreamsValidator
{
    public const int ListLevel = -1;

    public static IReadOnlyList<StreamIssue> Validate(IReadOnlyList<StreamSetting> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var issues = new List<StreamIssue>();
        if (items.Count > StreamsLimits.MaxEntries)
        {
            issues.Add(new StreamIssue(ListLevel, $"At most {StreamsLimits.MaxEntries} streams are allowed."));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            foreach (var message in ValidateEntry(item))
            {
                issues.Add(new StreamIssue(i, message));
            }

            if (!string.IsNullOrEmpty(item.StreamId) && !ids.Add(item.StreamId))
            {
                issues.Add(new StreamIssue(i, "Stream ID is used by another entry."));
            }
        }

        return issues;
    }

    public static IReadOnlyList<string> ValidateEntry(StreamSetting item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var messages = new List<string>();
        if (string.IsNullOrEmpty(item.StreamId) || !StreamIdPattern().IsMatch(item.StreamId))
        {
            messages.Add("Stream ID is invalid.");
        }

        messages.AddRange(ValidateFields(item.Title, item.Description, item.Url));
        return messages;
    }

    /// <summary>Regole su titolo, descrizione e URL. Usate anche per le voci di bootstrap.json.</summary>
    public static IReadOnlyList<string> ValidateFields(string? title, string? description, string? url)
    {
        var messages = new List<string>();

        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.Length == 0)
        {
            messages.Add("Title is required.");
        }
        else if (trimmedTitle.Length > StreamsLimits.MaxTitleLength)
        {
            messages.Add($"Title is longer than {StreamsLimits.MaxTitleLength} characters.");
        }

        if (description is not null && description.Trim().Length > StreamsLimits.MaxDescriptionLength)
        {
            messages.Add($"Description is longer than {StreamsLimits.MaxDescriptionLength} characters.");
        }

        var trimmedUrl = url?.Trim() ?? string.Empty;
        if (trimmedUrl.Length == 0)
        {
            messages.Add("URL is required.");
        }
        else if (trimmedUrl.Length > StreamsLimits.MaxUrlLength)
        {
            messages.Add($"URL is longer than {StreamsLimits.MaxUrlLength} characters.");
        }
        else if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host))
        {
            messages.Add("URL must start with https://.");
        }
        else if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            messages.Add("URL must not contain credentials (user:password@).");
        }

        return messages;
    }

    [GeneratedRegex("^[a-z0-9-]{1,64}$")]
    private static partial Regex StreamIdPattern();
}
