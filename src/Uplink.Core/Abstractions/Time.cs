namespace MyUniverseUplink.Core.Abstractions;

/// <summary>Sorgente del tempo, iniettabile nei test.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Sorgente di casualità per il jitter del backoff. Restituisce valori in [0, 1).</summary>
public interface IJitter
{
    double NextDouble();
}

public sealed class SystemJitter : IJitter
{
    public static readonly SystemJitter Instance = new();

    public double NextDouble() => Random.Shared.NextDouble();
}
