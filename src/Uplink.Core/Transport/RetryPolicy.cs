using MyUniverseUplink.Core.Abstractions;

namespace MyUniverseUplink.Core.Transport;

/// <summary>
/// Backoff esponenziale con jitter: min(maxDelay, base * 2^(tentativo-1)) * (0,5 + rnd/2).
/// Retry-After, se presente, fa da minimo ed e' rispettato anche oltre maxDelay.
/// </summary>
public sealed class RetryPolicy
{
    private readonly IJitter _jitter;

    public RetryPolicy(IJitter jitter, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null, int maxAttempts = 8)
    {
        ArgumentNullException.ThrowIfNull(jitter);
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        _jitter = jitter;
        BaseDelay = baseDelay ?? TimeSpan.FromSeconds(5);
        MaxDelay = maxDelay ?? TimeSpan.FromMinutes(15);
        MaxAttempts = maxAttempts;
    }

    public TimeSpan BaseDelay { get; }

    public TimeSpan MaxDelay { get; }

    /// <summary>Tentativi dopo i quali il batch va in dead-letter.</summary>
    public int MaxAttempts { get; }

    /// <param name="attempt">Numero del retry, a partire da 1.</param>
    /// <param name="retryAfter">Valore di Retry-After della risposta, se presente.</param>
    public TimeSpan GetDelay(int attempt, TimeSpan? retryAfter = null)
    {
        var exponent = Math.Min(Math.Max(attempt, 1) - 1, 30);
        var raw = BaseDelay.TotalSeconds * Math.Pow(2, exponent);
        var capped = Math.Min(MaxDelay.TotalSeconds, raw);
        var factor = 0.5 + _jitter.NextDouble() / 2.0;
        var delay = TimeSpan.FromSeconds(capped * factor);

        if (retryAfter is { } serverWait && serverWait > delay)
        {
            return serverWait;
        }

        return delay;
    }
}
