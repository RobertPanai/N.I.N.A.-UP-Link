using System.IO;
using System.Text;
using MyUniverseUplink.Core.Activity;
using MyUniverseUplink.Core.Settings;
using MyUniverseUplink.Runtime;

namespace MyUniverseUplink.Settings;

/// <summary>
/// Applica bootstrap.json alle impostazioni del profilo attivo, poi lo rimuove.
/// Se l'applicazione fallisce, resta una copia senza segreti in bootstrap.failed.json.
/// Nei log compaiono solo i nomi dei campi, mai i valori. Non lancia mai eccezioni.
/// </summary>
public sealed class BootstrapImporter
{
    private readonly PluginPaths _paths;
    private readonly ProfileSettingsStore _store;
    private readonly IUplinkLog _log;
    private readonly IActivitySink? _activity;

    public BootstrapImporter(PluginPaths paths, ProfileSettingsStore store, IUplinkLog log, IActivitySink? activity = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _activity = activity;
    }

    /// <summary>True se almeno un campo e' stato applicato al profilo attivo.</summary>
    public bool TryImport()
    {
        try
        {
            var file = _paths.BootstrapFile;
            if (!File.Exists(file))
            {
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _log.Error("bootstrap: bootstrap.json could not be read: " + ex.GetType().Name);
                return false;
            }

            var parsed = BootstrapSettings.Parse(text);
            if (parsed is null)
            {
                _log.Warn("bootstrap: bootstrap.json is not a JSON object; file removed, nothing applied");
                Delete(file);
                return false;
            }

            if (parsed.Rejected.Count > 0)
            {
                _log.Warn("bootstrap: invalid fields discarded: " + Names(parsed.Rejected));
                _activity?.Publish(ActivitySeverity.Warn, "bootstrap", "invalid fields discarded: " + Names(parsed.Rejected));
            }

            if (parsed.Ignored.Count > 0)
            {
                _log.Info("bootstrap: unknown fields ignored: " + Names(parsed.Ignored));
            }

            if (parsed.Applied.Count == 0)
            {
                _log.Info("bootstrap: no usable field; file removed");
                Delete(file);
                return false;
            }

            try
            {
                Apply(parsed.Settings);
            }
            catch (Exception ex)
            {
                _log.Error("bootstrap: not applied (" + ex.GetType().Name + "); copy without secrets kept as bootstrap.failed.json");
                _activity?.Publish(ActivitySeverity.Error, "bootstrap", "not applied (" + ex.GetType().Name + "); plugin settings unchanged");
                KeepWithoutSecrets(file, text);
                return false;
            }

            Delete(file);
            _log.Info("bootstrap: applied to the active profile: " + Names(parsed.Applied));
            _activity?.Publish(ActivitySeverity.Ok, "bootstrap", "applied to the active profile, fields: " + Names(parsed.Applied));
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("bootstrap: import stopped (" + ex.GetType().Name + "); plugin settings unchanged");
            return false;
        }
    }

    private void Apply(BootstrapSettings s)
    {
        var current = _store.Load();
        if (string.IsNullOrEmpty(current.ProfileId))
        {
            throw new InvalidOperationException("no active profile");
        }

        var config = current with
        {
            Enabled = s.Enabled ?? current.Enabled,
            ServerUrl = s.ServerUrl ?? current.ServerUrl,
            DeviceId = s.DeviceId ?? current.DeviceId,
            BatchSize = s.BatchSize ?? current.BatchSize,
            ThumbnailMode = s.ThumbnailMode ?? current.ThumbnailMode,
            ThumbnailLongEdgePx = s.ThumbnailLongEdgePx ?? current.ThumbnailLongEdgePx,
            ThumbnailJpegQuality = s.ThumbnailJpegQuality ?? current.ThumbnailJpegQuality,
            ExtraHeaderName = s.ExtraHeaderName ?? current.ExtraHeaderName,
            ImageTypes = s.ImageTypes ?? current.ImageTypes,
            RequireTarget = s.RequireTarget ?? current.RequireTarget,
            HtmlReport = s.HtmlReport ?? current.HtmlReport,
        };

        _store.SaveSettings(config);
        if (s.Token is not null)
        {
            _store.SaveToken(s.Token);
        }

        if (s.ExtraHeaderValue is not null)
        {
            _store.SaveExtraHeaderValue(s.ExtraHeaderValue);
        }
    }

    /// <summary>Dopo un errore: copia senza token e valore header in bootstrap.failed.json, poi rimuove l'originale.</summary>
    private void KeepWithoutSecrets(string file, string text)
    {
        try
        {
            var clean = BootstrapSettings.RemoveSecrets(text);
            if (clean is not null)
            {
                File.WriteAllText(_paths.BootstrapFailedFile, clean, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (Exception ex)
        {
            _log.Warn("bootstrap: bootstrap.failed.json could not be written: " + ex.GetType().Name);
        }
        finally
        {
            Delete(file);
        }
    }

    private void Delete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            _log.Warn("bootstrap: bootstrap.json could not be removed: " + ex.GetType().Name);
        }
    }

    private static string Names(IEnumerable<string> names) => string.Join(", ", names);
}
