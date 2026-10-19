using MyUniverseUplink.Core.Pipeline;
using MyUniverseUplink.Core.Settings;
using MyUniverseUplink.Runtime;
using NINA.Profile;
using NINA.Profile.Interfaces;

namespace MyUniverseUplink.Settings;

/// <summary>
/// Impostazioni per profilo NINA, tramite PluginOptionsAccessor (il profilo attivo viene scelto da NINA).
/// Token e valore dell'header extra sono cifrati con DPAPI prima di essere scritti.
/// </summary>
public sealed class ProfileSettingsStore
{
    private const string KeyEnabled = "Enabled";
    private const string KeyServerUrl = "ServerUrl";
    private const string KeyDeviceId = "DeviceId";
    private const string KeyBatchSize = "BatchSize";
    private const string KeyThumbMode = "ThumbnailMode";
    private const string KeyThumbEdge = "ThumbnailLongEdgePx";
    private const string KeyThumbQuality = "ThumbnailJpegQuality";
    private const string KeyImageTypes = "ImageTypes";
    private const string KeyRequireTarget = "RequireTarget";
    private const string KeyHtmlReport = "HtmlReport";
    private const string KeyExtraHeaderName = "ExtraHeaderName";
    private const string KeyToken = "TokenProtected";
    private const string KeyExtraHeaderValue = "ExtraHeaderValueProtected";
    private const string KeyStreamsEnabled = "StreamsEnabled";
    private const string KeyStreams = "Streams";

    private readonly PluginOptionsAccessor _options;
    private readonly IProfileService _profileService;
    private readonly IUplinkLog _log;

    public ProfileSettingsStore(PluginOptionsAccessor options, IProfileService profileService, IUplinkLog log)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Legge le impostazioni del profilo attivo. In caso di errore restituisce una configurazione disattivata.</summary>
    public PluginConfig Load()
    {
        try
        {
            var profile = _profileService.ActiveProfile;

            var storedToken = _options.GetValueString(KeyToken, string.Empty);
            var token = SecretProtector.Unprotect(storedToken);
            if (!string.IsNullOrEmpty(storedToken) && token is null)
            {
                _log.Warn("saved token could not be decrypted for this Windows user; enter it again");
            }

            var storedHeader = _options.GetValueString(KeyExtraHeaderValue, string.Empty);
            var headerValue = SecretProtector.Unprotect(storedHeader);

            var headerName = _options.GetValueString(KeyExtraHeaderName, string.Empty);
            var types = _options.GetValueString(KeyImageTypes, FrameAdmission.DefaultImageType)
                .Split(',', StringSplitOptions.RemoveEmptyEntries);

            if (!StreamsSettingsJson.TryParse(_options.GetValueString(KeyStreams, "[]"), out var streams))
            {
                _log.Warn("saved live streams could not be read; the list is empty");
            }

            return new PluginConfig
            {
                ProfileId = profile?.Id.ToString() ?? string.Empty,
                ProfileName = profile?.Name ?? string.Empty,
                Enabled = _options.GetValueBoolean(KeyEnabled, false),
                ServerUrl = _options.GetValueString(KeyServerUrl, string.Empty),
                DeviceId = _options.GetValueString(KeyDeviceId, string.Empty),
                BatchSize = _options.GetValueInt32(KeyBatchSize, 25),
                ThumbnailMode = ParseThumbnailMode(_options.GetValueString(KeyThumbMode, "Sampled")),
                ThumbnailLongEdgePx = _options.GetValueInt32(KeyThumbEdge, 400),
                ThumbnailJpegQuality = _options.GetValueInt32(KeyThumbQuality, 70),
                ImageTypes = FrameAdmission.NormalizeImageTypes(types),
                RequireTarget = _options.GetValueBoolean(KeyRequireTarget, true),
                HtmlReport = _options.GetValueBoolean(KeyHtmlReport, false),
                ExtraHeaderName = string.IsNullOrWhiteSpace(headerName) ? null : headerName.Trim(),
                ExtraHeaderValue = headerValue,
                BearerToken = token,
                StreamsEnabled = _options.GetValueBoolean(KeyStreamsEnabled, false),
                Streams = streams,
            };
        }
        catch (Exception ex)
        {
            _log.Error("could not read plugin settings; uplink stays off", ex);
            return new PluginConfig();
        }
    }

    /// <summary>Scrive le impostazioni non segrete del profilo attivo.</summary>
    public void SaveSettings(PluginConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _options.SetValueBoolean(KeyEnabled, config.Enabled);
        _options.SetValueString(KeyServerUrl, config.ServerUrl.Trim());
        _options.SetValueString(KeyDeviceId, config.DeviceId.Trim());
        _options.SetValueInt32(KeyBatchSize, config.BatchSize);
        _options.SetValueString(KeyThumbMode, config.ThumbnailMode.ToString());
        _options.SetValueInt32(KeyThumbEdge, config.ThumbnailLongEdgePx);
        _options.SetValueInt32(KeyThumbQuality, config.ThumbnailJpegQuality);
        _options.SetValueString(KeyImageTypes, string.Join(",", FrameAdmission.NormalizeImageTypes(config.ImageTypes)));
        _options.SetValueBoolean(KeyRequireTarget, config.RequireTarget);
        _options.SetValueBoolean(KeyHtmlReport, config.HtmlReport);
        _options.SetValueString(KeyExtraHeaderName, config.ExtraHeaderName?.Trim() ?? string.Empty);
        _options.SetValueBoolean(KeyStreamsEnabled, config.StreamsEnabled);
        _options.SetValueString(KeyStreams, StreamsSettingsJson.Serialize(config.Streams));
        Persist();
    }

    /// <summary>Cifra e salva il token. Stringa vuota = rimuove il token.</summary>
    public void SaveToken(string? token)
    {
        var trimmed = token?.Trim();
        _options.SetValueString(KeyToken, string.IsNullOrEmpty(trimmed) ? string.Empty : SecretProtector.Protect(trimmed));
        Persist();
    }

    /// <summary>Cifra e salva il valore dell'header extra. Stringa vuota = rimuove il valore.</summary>
    public void SaveExtraHeaderValue(string? value)
    {
        var trimmed = value?.Trim();
        _options.SetValueString(KeyExtraHeaderValue, string.IsNullOrEmpty(trimmed) ? string.Empty : SecretProtector.Protect(trimmed));
        Persist();
    }

    /// <summary>Chiede a NINA di salvare il profilo attivo, così le impostazioni sopravvivono al riavvio.</summary>
    private void Persist()
    {
        try
        {
            _profileService.ActiveProfile?.Save();
        }
        catch (Exception ex)
        {
            _log.Error("profile save after plugin settings change failed", ex);
        }
    }

    private static ThumbnailMode ParseThumbnailMode(string? text)
        => Enum.TryParse<ThumbnailMode>(text, ignoreCase: true, out var mode) ? mode : ThumbnailMode.Sampled;
}
