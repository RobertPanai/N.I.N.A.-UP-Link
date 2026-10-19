using MyUniverseUplink.Core.Abstractions;

namespace MyUniverseUplink.Core.Activity;

/// <summary>
/// Feed in memoria degli eventi del plugin: buffer circolare di Capacity voci, thread-safe.
/// Publish sotto lock aggiunge la voce e notifica EntryAdded fuori dal lock. Un subscriber che lancia
/// eccezioni non interrompe mai chi pubblica.
/// </summary>
public sealed class ActivityFeed : IActivitySink
{
    public const int Capacity = 500;

    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly ActivityEntry[] _buffer = new ActivityEntry[Capacity];
    private int _start;
    private int _count;
    private long _nextId = 1;

    public ActivityFeed(IClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>Notificata per ogni voce, nel thread che l'ha pubblicata. Il gestore deve essere veloce.</summary>
    public event EventHandler<ActivityEntry>? EntryAdded;

    public void Publish(ActivitySeverity severity, string category, string message)
    {
        ActivityEntry entry;
        lock (_gate)
        {
            entry = new ActivityEntry(
                _nextId++,
                _clock.UtcNow,
                severity,
                string.IsNullOrWhiteSpace(category) ? "general" : category.Trim(),
                ActivityText.Clean(message));

            if (_count < Capacity)
            {
                _buffer[(_start + _count) % Capacity] = entry;
                _count++;
            }
            else
            {
                _buffer[_start] = entry;
                _start = (_start + 1) % Capacity;
            }
        }

        var handlers = EntryAdded;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<ActivityEntry>>())
        {
            try
            {
                handler(this, entry);
            }
            catch (Exception)
            {
                // Un subscriber difettoso non deve far fallire il plugin ne' fermare gli altri.
            }
        }
    }

    /// <summary>Copia delle voci in ordine cronologico (dalla piu' vecchia alla piu' recente).</summary>
    public IReadOnlyList<ActivityEntry> Snapshot()
    {
        lock (_gate)
        {
            var list = new ActivityEntry[_count];
            for (var i = 0; i < _count; i++)
            {
                list[i] = _buffer[(_start + i) % Capacity];
            }

            return list;
        }
    }

    /// <summary>Svuota il buffer. Gli id continuano a crescere.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_buffer);
            _start = 0;
            _count = 0;
        }
    }
}
