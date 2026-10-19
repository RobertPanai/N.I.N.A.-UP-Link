namespace MyUniverseUplink.Core.Activity;

/// <summary>Gravita' di un evento del feed. L'ordine va dal meno al piu' grave (usato dai filtri).</summary>
public enum ActivitySeverity
{
    Info = 0,
    Ok = 1,
    Warn = 2,
    Error = 3,
}

public static class ActivityFormat
{
    /// <summary>Etichetta di 5 caratteri per la colonna di gravita' (allineata in monospace).</summary>
    public static string Label(this ActivitySeverity severity) => severity switch
    {
        ActivitySeverity.Info => "INFO ",
        ActivitySeverity.Ok => "OK   ",
        ActivitySeverity.Warn => "WARN ",
        ActivitySeverity.Error => "ERROR",
        _ => "?    ",
    };
}
