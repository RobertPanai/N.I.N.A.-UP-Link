using System.ComponentModel.Composition;
using MyUniverseUplink.Runtime;
using MyUniverseUplink.Services;
using MyUniverseUplink.Settings;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;

namespace MyUniverseUplink;

/// <summary>Plugin NINA: invia metadati e miniature dei frame salvati al sito (contratto Uplink v1).</summary>
[Export(typeof(IPluginManifest))]
public sealed class MyUniverseUplinkPlugin : PluginBase
{
    /// <summary>GUID dell'assembly (Properties/AssemblyInfo.cs). Chiave delle impostazioni per profilo.</summary>
    public static readonly Guid PluginGuid = new("fb4fd376-f1fb-48a2-a6fa-c4a7b278472e");

    private readonly IProfileService _profileService;
    private readonly IImageSaveMediator _imageSaveMediator;
    private NinaLogBridge? _log;
    private ProfileSettingsStore? _store;
    private UplinkRuntime? _runtime;
    private ImageSavedListener? _listener;
    private BootstrapImporter? _importer;

    /// <summary>Istanza attiva, usata dalla pagina Opzioni. Null prima di Initialize e dopo Teardown.</summary>
    public static MyUniverseUplinkPlugin? Instance { get; private set; }

    public ProfileSettingsStore? Store => _store;

    public UplinkRuntime? Runtime => _runtime;

    public NinaLogBridge? Log => _log;

    /// <summary>Le impostazioni del profilo attivo sono cambiate (bootstrap o cambio profilo): la pagina Opzioni si ricarica.</summary>
    public event EventHandler? SettingsChanged;

    [ImportingConstructor]
    public MyUniverseUplinkPlugin(IProfileService profileService, IImageSaveMediator imageSaveMediator)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _imageSaveMediator = imageSaveMediator ?? throw new ArgumentNullException(nameof(imageSaveMediator));
    }

    public override async Task Initialize()
    {
        try
        {
            Instance = this;
            var paths = PluginPaths.Default;
            paths.EnsureCreated();

            _log = new NinaLogBridge(new FileLog(paths.Logs));
            _store = new ProfileSettingsStore(new PluginOptionsAccessor(_profileService, PluginGuid), _profileService, _log);
            _importer = new BootstrapImporter(paths, _store, _log);
            _runtime = new UplinkRuntime(paths, _log);
            _runtime.Start();
            RunBootstrap();
            await _runtime.ApplyAsync(_store.Load()).ConfigureAwait(false);

            _listener = new ImageSavedListener(_imageSaveMediator, _runtime.HandleImageSaved, _log);
            _listener.Start();
            _profileService.ProfileChanged += OnProfileChanged;

            _log.Info("My Universe Uplink " + PluginInfo.PluginVersion + " initialized (NINA " + PluginInfo.NinaVersion + ")");
        }
        catch (Exception ex)
        {
            _log?.Error("Initialize failed; the plugin stays inactive", ex);
        }
    }

    public override async Task Teardown()
    {
        try
        {
            _profileService.ProfileChanged -= OnProfileChanged;
            _listener?.Stop();
            if (_runtime is not null)
            {
                await _runtime.StopAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log?.Error("Teardown failed", ex);
        }
        finally
        {
            _runtime?.Dispose();
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }
    }

    /// <summary>Cambio profilo: rilegge le impostazioni del nuovo profilo e riavvia il worker. Non blocca NINA.</summary>
    private void OnProfileChanged(object? sender, EventArgs e)
    {
        try
        {
            var store = _store;
            var runtime = _runtime;
            if (store is null || runtime is null)
            {
                return;
            }

            RunBootstrap();
            var config = store.Load();
            _ = runtime.ApplyAsync(config);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log?.Error("profile change: could not reload settings", ex);
        }
    }

    /// <summary>Applica bootstrap.json se presente. Non lancia: un bootstrap difettoso non blocca il plugin.</summary>
    private void RunBootstrap()
    {
        try
        {
            if (_importer is not null && _importer.TryImport())
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _log?.Error("bootstrap: unexpected failure (" + ex.GetType().Name + "); plugin settings unchanged");
        }
    }
}
