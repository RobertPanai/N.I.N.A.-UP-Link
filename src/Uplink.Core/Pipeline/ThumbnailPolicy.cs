using MyUniverseUplink.Core.Identity;
using MyUniverseUplink.Core.Settings;

namespace MyUniverseUplink.Core.Pipeline;

/// <summary>Un frame candidato a miniatura.</summary>
public sealed record ThumbnailContext(string? TargetName, string? Filter, DateTimeOffset CapturedAtUtc, bool Force = false);

/// <summary>
/// Decide se inviare la miniatura. Sampled: la prima per target/filtro/notte, poi una ogni
/// <c>sampleInterval</c>; tetto <c>maxPerNight</c> per notte. Force salta il campionamento e il tetto.
/// Never non invia mai, nemmeno con Force.
/// ShouldSend registra la decisione: va chiamato una volta per frame.
/// </summary>
public sealed class ThumbnailPolicy
{
    private readonly ThumbnailMode _mode;
    private readonly TimeSpan _sampleInterval;
    private readonly int _maxPerNight;
    private readonly HashSet<string> _seenThisNight = new(StringComparer.Ordinal);
    private string? _night;
    private int _countThisNight;
    private DateTimeOffset? _lastSent;

    public ThumbnailPolicy(ThumbnailMode mode, TimeSpan? sampleInterval = null, int maxPerNight = 60)
    {
        if (maxPerNight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPerNight));
        }

        _mode = mode;
        _sampleInterval = sampleInterval ?? TimeSpan.FromMinutes(15);
        _maxPerNight = maxPerNight;
    }

    public bool ShouldSend(ThumbnailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (_mode)
        {
            case ThumbnailMode.Never:
                return false;
            case ThumbnailMode.Always:
                return true;
        }

        if (context.Force)
        {
            RecordSent(context.CapturedAtUtc);
            return true;
        }

        var night = NightId.For(context.CapturedAtUtc);
        if (!string.Equals(night, _night, StringComparison.Ordinal))
        {
            _night = night;
            _seenThisNight.Clear();
            _countThisNight = 0;
        }

        if (_countThisNight >= _maxPerNight)
        {
            return false;
        }

        var key = (context.TargetName?.Trim() ?? string.Empty).ToUpperInvariant()
                  + "|" + (context.Filter?.Trim() ?? string.Empty).ToUpperInvariant();
        var firstOfKind = _seenThisNight.Add(key);
        var due = _lastSent is null || context.CapturedAtUtc - _lastSent.Value >= _sampleInterval;

        if (firstOfKind || due)
        {
            RecordSent(context.CapturedAtUtc);
            return true;
        }

        return false;
    }

    private void RecordSent(DateTimeOffset capturedAtUtc)
    {
        _countThisNight++;
        _lastSent = capturedAtUtc;
    }
}
