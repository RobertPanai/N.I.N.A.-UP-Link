using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using MyUniverseUplink.Core.Pipeline;
using MyUniverseUplink.Core.Settings;
using MyUniverseUplink.Runtime;
using MyUniverseUplink.Services;
using MyUniverseUplink.Settings;

namespace MyUniverseUplink.Options;

/// <summary>
/// ViewModel della pagina Opzioni. Il token e il valore dell'header arrivano dalle PasswordBox come parametro:
/// non vengono mai esposti come proprieta' ne' scritti nei log.
/// Le impostazioni di testo si salvano con "Save settings"; token e header con i pulsanti dedicati.
/// </summary>
public sealed class OptionsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MyUniverseUplinkPlugin _plugin;
    private readonly DispatcherTimer _statusTimer;
    private readonly Dispatcher _dispatcher;
    private PluginConfig _saved;
    private bool _loading;

    private bool _enabled;
    private string _serverUrl = string.Empty;
    private string _deviceId = string.Empty;
    private string _extraHeaderName = string.Empty;
    private bool _hasToken;
    private bool _hasHeaderValue;
    private bool _sendLight = true;
    private bool _sendFlat;
    private bool _sendDark;
    private bool _sendBias;
    private bool _requireTarget = true;
    private string _selectedThumbnailMode = nameof(ThumbnailMode.Sampled);
    private string _thumbnailEdge = "400";
    private string _thumbnailQuality = "70";
    private bool _htmlReport;
    private string _validation = string.Empty;
    private string _testResult = string.Empty;
    private string _statusText = string.Empty;
    private string _actionMessage = string.Empty;
    private bool _disposed;

    public OptionsViewModel(MyUniverseUplinkPlugin plugin)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _dispatcher = Dispatcher.CurrentDispatcher;
        _saved = plugin.Store?.Load() ?? new PluginConfig();
        plugin.SettingsChanged += OnSettingsChanged;

        _loading = true;
        LoadFrom(_saved);
        _loading = false;

        SaveCommand = new RelayCommand(_ => Save());
        SendLastCommand = new RelayCommand(_ => Safe(SendLast));
        FlushCommand = new RelayCommand(_ => Safe(Flush));
        RetryDeadCommand = new RelayCommand(_ => Safe(RetryDead));
        OpenFolderCommand = new RelayCommand(_ => Safe(OpenFolder));

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> ThumbnailModes { get; } = Enum.GetNames<ThumbnailMode>();

    public RelayCommand SaveCommand { get; }

    public RelayCommand SendLastCommand { get; }

    public RelayCommand FlushCommand { get; }

    public RelayCommand RetryDeadCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public string ServerUrl
    {
        get => _serverUrl;
        set => Set(ref _serverUrl, value);
    }

    public string DeviceId
    {
        get => _deviceId;
        set => Set(ref _deviceId, value);
    }

    public string ExtraHeaderName
    {
        get => _extraHeaderName;
        set => Set(ref _extraHeaderName, value);
    }

    public string TokenState => _hasToken
        ? "A token is saved (encrypted for this Windows user). Type a new one to replace it."
        : "No token saved yet.";

    public string HeaderState => _hasHeaderValue
        ? "A header value is saved (encrypted). Clear the box and save to remove it."
        : "No header value saved.";

    public bool SendLight
    {
        get => _sendLight;
        set => Set(ref _sendLight, value);
    }

    public bool SendFlat
    {
        get => _sendFlat;
        set => Set(ref _sendFlat, value);
    }

    public bool SendDark
    {
        get => _sendDark;
        set => Set(ref _sendDark, value);
    }

    public bool SendBias
    {
        get => _sendBias;
        set => Set(ref _sendBias, value);
    }

    public bool RequireTarget
    {
        get => _requireTarget;
        set => Set(ref _requireTarget, value);
    }

    public string SelectedThumbnailMode
    {
        get => _selectedThumbnailMode;
        set => Set(ref _selectedThumbnailMode, value);
    }

    public string ThumbnailEdge
    {
        get => _thumbnailEdge;
        set => Set(ref _thumbnailEdge, value);
    }

    public string ThumbnailQuality
    {
        get => _thumbnailQuality;
        set => Set(ref _thumbnailQuality, value);
    }

    /// <summary>Salvato subito: non richiede "Save settings".</summary>
    public bool HtmlReport
    {
        get => _htmlReport;
        set
        {
            if (Set(ref _htmlReport, value) && !_loading)
            {
                SaveHtmlReportFlag(value);
            }
        }
    }

    public string Validation
    {
        get => _validation;
        private set => Set(ref _validation, value);
    }

    public string TestResult
    {
        get => _testResult;
        private set => Set(ref _testResult, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string ActionMessage
    {
        get => _actionMessage;
        private set => Set(ref _actionMessage, value);
    }

    /// <summary>Salva le impostazioni di testo e di invio dai campi, se valide, e riavvia il worker.</summary>
    public void Save()
    {
        try
        {
            var config = BuildFromFields(_saved);
            if (config.Enabled)
            {
                var errors = SettingsValidator.Validate(config.ToUplinkSettings());
                if (errors.Count > 0)
                {
                    Validation = string.Join(" ", errors);
                    return;
                }
            }

            Store.SaveSettings(config);
            _saved = config;
            Validation = "Settings saved.";
            _ = Runtime.ApplyAsync(config);
        }
        catch (Exception ex)
        {
            Validation = "Settings not saved: " + ex.GetType().Name;
            _plugin.Log?.Error("options: save failed", ex);
        }
    }

    /// <summary>Salva il token digitato (cifrato). Il testo non viene mai ricopiato nei campi.</summary>
    public void SaveToken(string? typed)
    {
        try
        {
            var token = typed?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                ActionMessage = "Type a token first.";
                return;
            }

            Store.SaveToken(token);
            ReloadSecrets();
            ActionMessage = "Token saved (encrypted for this Windows user).";
            _ = Runtime.ApplyAsync(_saved);
        }
        catch (Exception ex)
        {
            ActionMessage = "Token not saved: " + ex.GetType().Name;
            _plugin.Log?.Error("options: token save failed", ex);
        }
    }

    /// <summary>Salva il valore dell'header extra (cifrato). Casella vuota = rimuove il valore.</summary>
    public void SaveHeaderValue(string? typed)
    {
        try
        {
            Store.SaveExtraHeaderValue(typed);
            ReloadSecrets();
            ActionMessage = string.IsNullOrWhiteSpace(typed) ? "Header value removed." : "Header value saved (encrypted).";
            _ = Runtime.ApplyAsync(_saved);
        }
        catch (Exception ex)
        {
            ActionMessage = "Header value not saved: " + ex.GetType().Name;
            _plugin.Log?.Error("options: header save failed", ex);
        }
    }

    /// <summary>GET health con i campi correnti. Se il token e' vuoto usa quello gia' salvato.</summary>
    public async Task TestConnectionAsync(string? typedToken)
    {
        TestResult = "Testing...";
        try
        {
            var typed = typedToken?.Trim();
            var basis = BuildFromFields(_saved);
            var config = basis with { BearerToken = string.IsNullOrEmpty(typed) ? _saved.BearerToken : typed };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            TestResult = await Runtime.TestConnectionAsync(config, cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            TestResult = "Test failed: " + ex.GetType().Name;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _plugin.SettingsChanged -= OnSettingsChanged;
        _statusTimer.Stop();
    }

    /// <summary>Esegue un comando dalla UI: se il plugin e' stato fermato l'eccezione diventa un messaggio, non un crash.</summary>
    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ActionMessage = "Action failed: " + ex.GetType().Name;
            _plugin.Log?.Error("options: action failed", ex);
        }
    }

    private ProfileSettingsStore Store => _plugin.Store ?? throw new InvalidOperationException("The plugin is not running.");

    private UplinkRuntime Runtime => _plugin.Runtime ?? throw new InvalidOperationException("The plugin is not running.");

    private void SendLast()
    {
        ActionMessage = Runtime.SendLastImage()
            ? "Last image queued again (with a thumbnail when the mode allows it)."
            : "No image received in this session yet.";
    }

    private void Flush()
    {
        Runtime.WakeWorker();
        ActionMessage = "Uploader woken up. A partial batch still waits up to 20 s.";
    }

    private void RetryDead()
    {
        var count = Runtime.RetryDead();
        ActionMessage = count + " dead frame(s) moved back to pending.";
    }

    private void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Runtime.Paths.Root) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ActionMessage = "Folder not opened: " + ex.GetType().Name;
        }
    }

    /// <summary>Puo' arrivare da un altro thread (bootstrap, cambio profilo): la ricarica torna sul thread della UI.</summary>
    private void OnSettingsChanged(object? sender, EventArgs e)
        => _dispatcher.BeginInvoke(new Action(ReloadFromStore));

    private void ReloadFromStore()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _saved = Store.Load();
            _loading = true;
            try
            {
                LoadFrom(_saved);
            }
            finally
            {
                _loading = false;
            }

            ActionMessage = "Settings updated from the active profile.";
        }
        catch (Exception ex)
        {
            ActionMessage = "Settings not reloaded: " + ex.GetType().Name;
            _plugin.Log?.Error("options: reload failed", ex);
        }
    }

    private void SaveHtmlReportFlag(bool value)
    {
        try
        {
            var config = _saved with { HtmlReport = value };
            Store.SaveSettings(config);
            _saved = config;
            _ = Runtime.ApplyAsync(config);
        }
        catch (Exception ex)
        {
            ActionMessage = "Report setting not saved: " + ex.GetType().Name;
            _plugin.Log?.Error("options: report flag save failed", ex);
        }
    }

    private void ReloadSecrets()
    {
        _saved = Store.Load();
        _hasToken = !string.IsNullOrEmpty(_saved.BearerToken);
        _hasHeaderValue = !string.IsNullOrEmpty(_saved.ExtraHeaderValue);
        RaisePropertyChanged(nameof(TokenState));
        RaisePropertyChanged(nameof(HeaderState));
    }

    private void LoadFrom(PluginConfig config)
    {
        Enabled = config.Enabled;
        ServerUrl = config.ServerUrl;
        DeviceId = config.DeviceId;
        ExtraHeaderName = config.ExtraHeaderName ?? string.Empty;
        SendLight = config.ImageTypes.Contains("LIGHT", StringComparer.OrdinalIgnoreCase);
        SendFlat = config.ImageTypes.Contains("FLAT", StringComparer.OrdinalIgnoreCase);
        SendDark = config.ImageTypes.Contains("DARK", StringComparer.OrdinalIgnoreCase);
        SendBias = config.ImageTypes.Contains("BIAS", StringComparer.OrdinalIgnoreCase);
        RequireTarget = config.RequireTarget;
        SelectedThumbnailMode = config.ThumbnailMode.ToString();
        ThumbnailEdge = config.ThumbnailLongEdgePx.ToString(CultureInfo.InvariantCulture);
        ThumbnailQuality = config.ThumbnailJpegQuality.ToString(CultureInfo.InvariantCulture);
        HtmlReport = config.HtmlReport;
        _hasToken = !string.IsNullOrEmpty(config.BearerToken);
        _hasHeaderValue = !string.IsNullOrEmpty(config.ExtraHeaderValue);
        RaisePropertyChanged(nameof(TokenState));
        RaisePropertyChanged(nameof(HeaderState));
    }

    private PluginConfig BuildFromFields(PluginConfig basis)
    {
        var types = new List<string>();
        if (SendLight)
        {
            types.Add("LIGHT");
        }

        if (SendFlat)
        {
            types.Add("FLAT");
        }

        if (SendDark)
        {
            types.Add("DARK");
        }

        if (SendBias)
        {
            types.Add("BIAS");
        }

        var mode = Enum.TryParse<ThumbnailMode>(SelectedThumbnailMode, out var parsed) ? parsed : ThumbnailMode.Sampled;

        return basis with
        {
            Enabled = Enabled,
            ServerUrl = ServerUrl.Trim(),
            DeviceId = DeviceId.Trim(),
            ExtraHeaderName = string.IsNullOrWhiteSpace(ExtraHeaderName) ? null : ExtraHeaderName.Trim(),
            ImageTypes = FrameAdmission.NormalizeImageTypes(types),
            RequireTarget = RequireTarget,
            ThumbnailMode = mode,
            ThumbnailLongEdgePx = ParseInt(ThumbnailEdge, -1),
            ThumbnailJpegQuality = ParseInt(ThumbnailQuality, -1),
        };
    }

    private void RefreshStatus()
    {
        var runtime = _plugin.Runtime;
        if (runtime is null)
        {
            StatusText = "The plugin is not running.";
            return;
        }

        var status = runtime.GetStatus();
        StatusText = string.Join(
            Environment.NewLine,
            "State: " + status.State,
            $"Pending: {status.Pending}    Dead: {status.Dead}    Sent tonight: {status.SentTonight}    Sent total: {status.Sent}",
            "Last success: " + FormatTime(status.LastSuccessAtUtc),
            "Last error: " + (status.LastError is null ? "-" : status.LastError + " (" + FormatTime(status.LastErrorAtUtc) + ")"));
    }

    private static string FormatTime(DateTimeOffset? utc)
        => utc is { } value ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "-";

    private static int ParseInt(string? text, int fallback)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(name);
        return true;
    }

    private void RaisePropertyChanged(string? name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
