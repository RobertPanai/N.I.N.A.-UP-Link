using MyUniverseUplink.Core.Model;

namespace MyUniverseUplink.Core.Transport;

public enum SendOutcome
{
    /// <summary>200 con corpo leggibile: esito per frame da applicare.</summary>
    Acknowledged,

    /// <summary>Rete, timeout, 5xx, 408, risposta non leggibile: si ritenta con backoff.</summary>
    Transient,

    /// <summary>429: si ritenta rispettando Retry-After.</summary>
    RateLimited,

    /// <summary>401/403: il worker si ferma. Nessun retry con un token sbagliato.</summary>
    AuthError,

    /// <summary>413: il batch va dimezzato.</summary>
    PayloadTooLarge,

    /// <summary>400/404/415/422 e altri 4xx: i frame vanno in dead-letter.</summary>
    Permanent,
}

public sealed record SendClassification(SendOutcome Outcome, TimeSpan? RetryAfter, string Reason);

/// <summary>Traduce l'esito HTTP nell'azione del worker (tabella di docs/plan-v1.md, sez. 8.2 e 10.4).</summary>
public static class ErrorClassifier
{
    public static SendClassification Classify<T>(UplinkResponse<T> response) where T : class
    {
        ArgumentNullException.ThrowIfNull(response);

        switch (response.Failure)
        {
            case TransportFailure.Timeout:
                return new SendClassification(SendOutcome.Transient, null, "HTTP timeout");
            case TransportFailure.Network:
                return new SendClassification(SendOutcome.Transient, null, "network error: " + response.ErrorMessage);
        }

        if (response.StatusCode is not { } status)
        {
            return new SendClassification(SendOutcome.Transient, null, "no HTTP response");
        }

        return status switch
        {
            >= 200 and < 300 when response.Body is not null
                => new SendClassification(SendOutcome.Acknowledged, null, "HTTP " + status),
            >= 200 and < 300
                => new SendClassification(SendOutcome.Transient, null, "HTTP " + status + " with unreadable body"),
            401 or 403
                => new SendClassification(SendOutcome.AuthError, null, "HTTP " + status + ": token rejected"),
            413
                => new SendClassification(SendOutcome.PayloadTooLarge, null, "HTTP 413: payload too large"),
            429
                => new SendClassification(SendOutcome.RateLimited, response.RetryAfter, "HTTP 429: rate limited"),
            408 or >= 500 and <= 599
                => new SendClassification(SendOutcome.Transient, null, "HTTP " + status),
            >= 300 and < 400
                => new SendClassification(SendOutcome.Transient, null, "HTTP " + status + " redirect"),
            _
                => new SendClassification(SendOutcome.Permanent, null, "HTTP " + status + ": rejected"),
        };
    }
}
