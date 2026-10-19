using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MyUniverseUplink.Core.Settings;
using MyUniverseUplink.Core.Streams;
using MyUniverseUplink.Services;

namespace MyUniverseUplink.Options;

/// <summary>
/// Sezione "Live streams" della pagina Opzioni: interruttore, voci in ordine di lista, validazione inline e stato
/// dell'ultima sincronizzazione. Le regole sono quelle del Core (StreamsValidator); qui solo presentazione.
/// </summary>
public sealed class StreamsEditorViewModel : INotifyPropertyChanged
{
    private readonly OptionsViewModel _owner;
    private bool _enabled;
    private string _message = string.Empty;
    private string _listIssue = string.Empty;
    private string _statusText = "not published";

    public StreamsEditorViewModel(OptionsViewModel owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Items = new ObservableCollection<StreamItemViewModel>();
        AddCommand = new RelayCommand(_ => Add());
        SyncNowCommand = new RelayCommand(_ => _owner.SyncStreamsNow());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public ObservableCollection<StreamItemViewModel> Items { get; }

    public RelayCommand AddCommand { get; }

    public RelayCommand SyncNowCommand { get; }

    public string Message
    {
        get => _message;
        internal set => Set(ref _message, value);
    }

    /// <summary>Problemi che riguardano l'insieme (es. piu' di 12 voci).</summary>
    public string ListIssue
    {
        get => _listIssue;
        private set => Set(ref _listIssue, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    internal void Load(bool enabled, IEnumerable<StreamSetting> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(new StreamItemViewModel(this, item.StreamId, item.Title, item.Description, item.Url, item.Enabled));
        }

        Enabled = enabled;
        Message = string.Empty;
        Revalidate();
    }

    internal StreamsSettings ToSettings() => new(Enabled, Items.Select(i => i.ToSetting()).ToList());

    /// <summary>Testo del problema da mostrare prima di salvare, o null se tutte le voci sono valide.</summary>
    internal string? Problem()
    {
        Revalidate();
        if (ListIssue.Length == 0 && Items.All(i => i.Issue.Length == 0))
        {
            return null;
        }

        return ListIssue.Length == 0
            ? "fix the entries marked in red"
            : "fix the entries marked in red; " + ListIssue;
    }

    internal void Revalidate()
    {
        var issues = StreamsValidator.Validate(ToSettings().Items);
        var perItem = new string[Items.Count];
        var listLevel = new List<string>();
        foreach (var issue in issues)
        {
            if (issue.Index >= 0 && issue.Index < Items.Count)
            {
                perItem[issue.Index] = string.IsNullOrEmpty(perItem[issue.Index])
                    ? issue.Message
                    : perItem[issue.Index] + " " + issue.Message;
            }
            else
            {
                listLevel.Add(issue.Message);
            }
        }

        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].Issue = perItem[i] ?? string.Empty;
        }

        ListIssue = string.Join(" ", listLevel);
    }

    internal void MoveItem(StreamItemViewModel item, int delta)
    {
        var index = Items.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Items.Count)
        {
            return;
        }

        Items.Move(index, target);
        Revalidate();
    }

    internal void Remove(StreamItemViewModel item)
    {
        Items.Remove(item);
        Revalidate();
    }

    internal void Refresh(StreamsStatus? status)
    {
        if (status is null)
        {
            StatusText = "not available: the plugin is not running";
            return;
        }

        var when = status.LastSuccessUtc is { } success
            ? " at " + success.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : string.Empty;

        StatusText = status.State switch
        {
            StreamsState.Published => "published " + status.Stored + when
                + (status.Visible == false ? " (section hidden on the site)" : string.Empty),
            StreamsState.Error => "error: " + (status.LastError ?? "unknown"),
            StreamsState.Suspended => "suspended: " + (status.LastError ?? "token rejected"),
            _ => "not published" + (status.Rejected > 0 ? " (" + status.Rejected + " rejected)" : string.Empty),
        };
    }

    private void Add()
    {
        if (Items.Count >= StreamsLimits.MaxEntries)
        {
            Message = "At most " + StreamsLimits.MaxEntries + " streams are allowed.";
            return;
        }

        var taken = new HashSet<string>(Items.Select(i => i.StreamId), StringComparer.Ordinal);
        Items.Add(new StreamItemViewModel(this, StreamIds.Create(string.Empty, taken), string.Empty, string.Empty, string.Empty, true));
        Message = string.Empty;
        Revalidate();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>Una voce modificabile. StreamId resta fisso per tutta la vita della voce.</summary>
public sealed class StreamItemViewModel : INotifyPropertyChanged
{
    private readonly StreamsEditorViewModel _editor;
    private string _title;
    private string _description;
    private string _url;
    private bool _enabled;
    private string _issue = string.Empty;

    internal StreamItemViewModel(StreamsEditorViewModel editor, string streamId, string title, string? description, string url, bool enabled)
    {
        _editor = editor;
        StreamId = streamId;
        _title = title;
        _description = description ?? string.Empty;
        _url = url;
        _enabled = enabled;
        UpCommand = new RelayCommand(_ => _editor.MoveItem(this, -1));
        DownCommand = new RelayCommand(_ => _editor.MoveItem(this, 1));
        RemoveCommand = new RelayCommand(_ => _editor.Remove(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StreamId { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (Set(ref _title, value))
            {
                _editor.Revalidate();
            }
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (Set(ref _description, value))
            {
                _editor.Revalidate();
            }
        }
    }

    public string Url
    {
        get => _url;
        set
        {
            if (Set(ref _url, value))
            {
                _editor.Revalidate();
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
            {
                _editor.Revalidate();
            }
        }
    }

    /// <summary>Messaggi di validazione della voce (vuoto = valida).</summary>
    public string Issue
    {
        get => _issue;
        internal set => Set(ref _issue, value);
    }

    public RelayCommand UpCommand { get; }

    public RelayCommand DownCommand { get; }

    public RelayCommand RemoveCommand { get; }

    internal StreamSetting ToSetting() => new(
        StreamId,
        Title.Trim(),
        string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        Url.Trim(),
        Enabled);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
