namespace MyUniverseUplink.Core.Activity;

/// <summary>Una riga del feed. Id cresce di 1 a ogni pubblicazione: serve a non mostrare due volte la stessa voce.</summary>
public sealed record ActivityEntry(
    long Id,
    DateTimeOffset UtcTimestamp,
    ActivitySeverity Severity,
    string Category,
    string Message);
