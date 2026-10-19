using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using MyUniverseUplink.Core.Activity;
using MyUniverseUplink.Services;

namespace MyUniverseUplink.Options;

/// <summary>Filtro di gravita': mostra le voci con gravita' pari o superiore a Minimum.</summary>
public sealed record ActivityFilter(string Label, ActivitySeverity Minimum);

/// <summary>Riga del feed. Il testo e' gia' ripulito dal Core: niente token, URL o percorsi.</summary>
public sealed class ActivityLine
{
    public ActivityLine(ActivityEntry entry, Brush brush)
    {
        Id = entry.Id;
        Severity = entry.Severity;
        Time = entry.UtcTimestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Label = entry.Severity.Label();
        Category = entry.Category;
        Message = entry.Message;
        Brush = brush;
    }

    public long Id { get; }

    public ActivitySeverity Severity { get; }

    public string Time { get; }

    public string Label { get; }

    public string Category { get; }

    public string Message { get; }

    public Brush Brush { get; }

    public string ToText() => $"{Time} {Label} {Category,-8} {Message}";
}

/// <summary>
/// Vista del feed Activity. Le voci arrivano da qualsiasi thread: EntryAdded le accoda senza bloccare, e un timer
/// sul Dispatcher le travasa nella lista ogni 100 ms (al massimo 10 aggiornamenti al secondo).
/// </summary>
public sealed class ActivityViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxDrainPerTick = 200;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly ActivityFeed _feed;
    private readonly ConcurrentQueue<ActivityEntry> _incoming = new();
    private readonly ObservableCollection<ActivityLine> _lines = new();
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<ActivitySeverity, Brush> _brushes;
    private ActivityFilter _selectedFilter;
    private bool _follow = true;
    private long _lastId;
    private bool _disposed;

    public ActivityViewModel(ActivityFeed feed, Dispatcher dispatcher)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        ArgumentNullException.ThrowIfNull(dispatcher);

        Filters = new[]
        {
            new ActivityFilter("All", ActivitySeverity.Info),
            new ActivityFilter("OK and up", ActivitySeverity.Ok),
            new ActivityFilter("Warn and up", ActivitySeverity.Warn),
            new ActivityFilter("Errors only", ActivitySeverity.Error),
        };
        _selectedFilter = Filters[0];
        _brushes = BuildBrushes();

        _view = CollectionViewSource.GetDefaultView(_lines);
        _view.Filter = item => item is ActivityLine line && line.Severity >= _selectedFilter.Minimum;

        // Prima ci si iscrive, poi si legge lo snapshot: le voci nel mezzo si ritrovano in coda e i doppioni si scartano per Id.
        _feed.EntryAdded += OnEntryAdded;
        foreach (var entry in _feed.Snapshot())
        {
            AddLine(entry);
        }

        ClearCommand = new RelayCommand(_ => Clear());
        CopyCommand = new RelayCommand(_ => Copy());

        _timer = new DispatcherTimer(RefreshInterval, DispatcherPriority.Background, OnRefreshTick, dispatcher);
        _timer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Chiesto quando arrivano voci nuove con Follow attivo: la vista scorre all'ultima riga.</summary>
    public event EventHandler? ScrollRequested;

    public ICollectionView Lines => _view;

    public IReadOnlyList<ActivityFilter> Filters { get; }

    public ActivityFilter SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedFilter))
            {
                return;
            }

            _selectedFilter = value;
            _view.Refresh();
            RaisePropertyChanged(nameof(SelectedFilter));
        }
    }

    public bool Follow
    {
        get => _follow;
        set
        {
            if (_follow == value)
            {
                return;
            }

            _follow = value;
            RaisePropertyChanged(nameof(Follow));
            if (value)
            {
                RequestScroll();
            }
        }
    }

    public RelayCommand ClearCommand { get; }

    public RelayCommand CopyCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _feed.EntryAdded -= OnEntryAdded;
        _timer.Stop();
    }

    private void OnEntryAdded(object? sender, ActivityEntry entry) => _incoming.Enqueue(entry);

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var appended = false;
        for (var i = 0; i < MaxDrainPerTick && _incoming.TryDequeue(out var entry); i++)
        {
            if (entry.Id <= _lastId)
            {
                continue;
            }

            AddLine(entry);
            appended = true;
        }

        if (appended && _follow)
        {
            RequestScroll();
        }
    }

    private void AddLine(ActivityEntry entry)
    {
        _lastId = Math.Max(_lastId, entry.Id);
        _lines.Add(new ActivityLine(entry, _brushes[entry.Severity]));
        while (_lines.Count > ActivityFeed.Capacity)
        {
            _lines.RemoveAt(0);
        }
    }

    private void Clear()
    {
        _feed.Clear();
        while (_incoming.TryDequeue(out _))
        {
        }

        _lines.Clear();
        _feed.Publish(ActivitySeverity.Info, "action", "activity view cleared (activity.log keeps the history)");
    }

    private void Copy()
    {
        var lines = _view.Cast<ActivityLine>().ToList();
        var text = string.Join(Environment.NewLine, lines.Select(line => line.ToText()));
        try
        {
            Clipboard.SetText(text);
            _feed.Publish(ActivitySeverity.Info, "action", $"copied {lines.Count} line(s) to the clipboard");
        }
        catch (Exception)
        {
            _feed.Publish(ActivitySeverity.Warn, "action", "copy failed: clipboard busy, try again");
        }
    }

    private void RequestScroll() => ScrollRequested?.Invoke(this, EventArgs.Empty);

    private void RaisePropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static Dictionary<ActivitySeverity, Brush> BuildBrushes() => new()
    {
        [ActivitySeverity.Info] = ThemeBrush("ButtonForegroundBrush", Color.FromRgb(0x9A, 0xA7, 0xBD)),
        [ActivitySeverity.Ok] = ThemeBrush("SuccessBrush", Color.FromRgb(0x5F, 0xD3, 0x8D)),
        [ActivitySeverity.Warn] = ThemeBrush("WarningBrush", Color.FromRgb(0xE3, 0xB3, 0x41)),
        [ActivitySeverity.Error] = ThemeBrush("ErrorBrush", Color.FromRgb(0xFF, 0x6B, 0x6B)),
    };

    /// <summary>Colore dal tema di NINA se la chiave esiste; altrimenti il colore di fallback.</summary>
    private static Brush ThemeBrush(string key, Color fallback)
    {
        try
        {
            if (Application.Current?.FindResource(key) is Brush themed)
            {
                return themed;
            }
        }
        catch (Exception)
        {
            // Chiave assente nel tema: si usa il fallback.
        }

        var brush = new SolidColorBrush(fallback);
        brush.Freeze();
        return brush;
    }
}
