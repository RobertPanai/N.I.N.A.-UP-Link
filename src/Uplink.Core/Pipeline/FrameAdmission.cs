namespace MyUniverseUplink.Core.Pipeline;

/// <summary>Esito della regola di ammissione di un frame allo spool.</summary>
public sealed record AdmissionDecision(bool Accepted, string Reason);

/// <summary>
/// Regole di ammissione: tipo di immagine (confronto senza distinzione di maiuscole) e presenza di un target
/// (opzione). Il tipo di default e' solo LIGHT.
/// </summary>
public static class FrameAdmission
{
    public const string DefaultImageType = "LIGHT";

    /// <summary>Normalizza una lista di tipi (maiuscole, senza duplicati, senza vuoti). Lista vuota = solo LIGHT.</summary>
    public static IReadOnlyList<string> NormalizeImageTypes(IEnumerable<string>? types)
    {
        var list = (types ?? Array.Empty<string>())
            .Select(t => t?.Trim().ToUpperInvariant() ?? string.Empty)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (list.Count == 0)
        {
            list.Add(DefaultImageType);
        }

        return list;
    }

    public static AdmissionDecision Evaluate(
        string? imageType,
        bool hasTarget,
        IReadOnlyCollection<string> allowedTypes,
        bool requireTarget)
    {
        ArgumentNullException.ThrowIfNull(allowedTypes);

        var type = imageType?.Trim();
        if (string.IsNullOrEmpty(type) || !allowedTypes.Any(a => string.Equals(a, type, StringComparison.OrdinalIgnoreCase)))
        {
            return new AdmissionDecision(false, "image type not selected: " + (string.IsNullOrEmpty(type) ? "(none)" : type));
        }

        if (requireTarget && !hasTarget)
        {
            return new AdmissionDecision(false, "no target name");
        }

        return new AdmissionDecision(true, "accepted");
    }
}
