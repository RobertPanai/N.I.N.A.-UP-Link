using System.Globalization;
using System.Text;

namespace MyUniverseUplink.Core.Settings;

/// <summary>StreamId stabile: slug ASCII del titolo (max 50 caratteri) + 4 cifre esadecimali casuali.</summary>
public static class StreamIds
{
    private const int MaxSlugLength = 50;

    public static string Create(string? title, ISet<string>? taken = null, Random? random = null)
    {
        var rnd = random ?? Random.Shared;
        var slug = Slug(title);
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var candidate = slug + "-" + rnd.Next(0x10000).ToString("x4", CultureInfo.InvariantCulture);
            if (taken is null || !taken.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No free stream ID.");
    }

    private static string Slug(string? title)
    {
        var builder = new StringBuilder();
        foreach (var ch in (title ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(ch);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(lower);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength].TrimEnd('-');
        }

        return slug.Length == 0 ? "stream" : slug;
    }
}
