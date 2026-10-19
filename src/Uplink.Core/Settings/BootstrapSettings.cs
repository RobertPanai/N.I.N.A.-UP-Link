using System.Text.Json;
using System.Text.Json.Nodes;
using MyUniverseUplink.Core.Pipeline;

namespace MyUniverseUplink.Core.Settings;

/// <summary>
/// Impostazioni di bootstrap lette da bootstrap.json. Ogni campo e' opzionale: null = assente o scartato.
/// Parte pura, senza dipendenze da NINA. Un campo sconosciuto viene ignorato e annotato per nome;
/// un campo presente ma non valido viene scartato e annotato per nome. Il token e il valore dell'header extra
/// sono segreti: ToString non li mostra mai, e i nomi restituiti dal parsing non contengono mai valori.
/// </summary>
public sealed record BootstrapSettings
{
    private const string FieldEnabled = "enabled";
    private const string FieldServerUrl = "serverUrl";
    private const string FieldDeviceId = "deviceId";
    private const string FieldToken = "token";
    private const string FieldHeaderName = "extraHeaderName";
    private const string FieldHeaderValue = "extraHeaderValue";
    private const string FieldRequireTarget = "requireTarget";
    private const string FieldImageTypes = "imageTypes";
    private const string FieldThumbnailMode = "thumbnailMode";
    private const string FieldThumbnailEdge = "thumbnailLongEdge";
    private const string FieldThumbnailQuality = "thumbnailQuality";
    private const string FieldBatchSize = "batchSize";
    private const string FieldHtmlReport = "htmlReport";
    private const string FieldStreamsEnabled = "streamsEnabled";
    private const string FieldStreams = "streams";

    private static readonly string[] KnownFields =
    {
        FieldEnabled, FieldServerUrl, FieldDeviceId, FieldToken, FieldHeaderName, FieldHeaderValue,
        FieldRequireTarget, FieldImageTypes, FieldThumbnailMode, FieldThumbnailEdge, FieldThumbnailQuality,
        FieldBatchSize, FieldHtmlReport, FieldStreamsEnabled, FieldStreams,
    };

    /// <summary>Campi segreti: rimossi dalla copia di sicurezza bootstrap.failed.json.</summary>
    public static IReadOnlyList<string> SecretFieldNames { get; } = new[] { FieldToken, FieldHeaderValue };

    /// <summary>Tipi di immagine selezionabili dalla pagina Opzioni.</summary>
    private static readonly string[] AllowedImageTypes = { "LIGHT", "FLAT", "DARK", "BIAS" };

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public bool? Enabled { get; init; }

    public string? ServerUrl { get; init; }

    public string? DeviceId { get; init; }

    /// <summary>Segreto: solo in memoria, cifrato con DPAPI prima di essere salvato.</summary>
    public string? Token { get; init; }

    public string? ExtraHeaderName { get; init; }

    /// <summary>Segreto: solo in memoria, cifrato con DPAPI prima di essere salvato.</summary>
    public string? ExtraHeaderValue { get; init; }

    public bool? RequireTarget { get; init; }

    public IReadOnlyList<string>? ImageTypes { get; init; }

    public ThumbnailMode? ThumbnailMode { get; init; }

    public int? ThumbnailLongEdgePx { get; init; }

    public int? ThumbnailJpegQuality { get; init; }

    public int? BatchSize { get; init; }

    public bool? HtmlReport { get; init; }

    /// <summary>Interruttore delle dirette (streamsEnabled). Null = assente.</summary>
    public bool? StreamsEnabled { get; init; }

    /// <summary>Voci delle dirette (streams). Null = assente; lista vuota = valida e svuota le dirette.</summary>
    public IReadOnlyList<BootstrapStream>? Streams { get; init; }

    /// <summary>Legge un documento JSON. Null se il testo non e' un oggetto JSON. Campi e nomi sono case-insensitive.</summary>
    public static BootstrapParseResult? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = TryParseObject(json);
        if (document is null)
        {
            return null;
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var ignored = new List<string>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var field = KnownFields.FirstOrDefault(f => string.Equals(f, property.Name, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                ignored.Add(property.Name);
            }
            else
            {
                values[field] = property.Value;
            }
        }

        var reader = new FieldReader(values);
        var settings = new BootstrapSettings
        {
            Enabled = reader.Bool(FieldEnabled),
            ServerUrl = reader.Text(FieldServerUrl, v => Valid(s => s.ServerUrl = v)),
            DeviceId = reader.Text(FieldDeviceId, v => Valid(s => s.DeviceId = v)),
            Token = reader.Text(FieldToken, IsToken),
            RequireTarget = reader.Bool(FieldRequireTarget),
            ImageTypes = reader.ImageTypes(FieldImageTypes),
            ThumbnailMode = reader.Mode(FieldThumbnailMode),
            ThumbnailLongEdgePx = reader.Int(FieldThumbnailEdge, (s, v) => s.ThumbnailLongEdgePx = v),
            ThumbnailJpegQuality = reader.Int(FieldThumbnailQuality, (s, v) => s.ThumbnailJpegQuality = v),
            BatchSize = reader.Int(FieldBatchSize, (s, v) => s.BatchSize = v),
            HtmlReport = reader.Bool(FieldHtmlReport),
            StreamsEnabled = reader.Bool(FieldStreamsEnabled),
            Streams = reader.Streams(FieldStreams),
        };

        var (headerName, headerValue) = reader.Header(FieldHeaderName, FieldHeaderValue);
        settings = settings with { ExtraHeaderName = headerName, ExtraHeaderValue = headerValue };

        return new BootstrapParseResult(settings, reader.Applied, reader.Rejected, ignored);
    }

    /// <summary>
    /// Copia del documento senza i campi segreti (token, valore header), da conservare come bootstrap.failed.json.
    /// Null se il testo non e' un oggetto JSON (in quel caso non c'e' modo sicuro di conservarlo).
    /// </summary>
    public static string? RemoveSecrets(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json, null, ParseOptions) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is null)
        {
            return null;
        }

        foreach (var key in root.Select(p => p.Key).ToList())
        {
            if (SecretFieldNames.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                root.Remove(key);
            }
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public override string ToString()
    {
        var parts = new[]
        {
            "enabled=" + Show(Enabled),
            "serverUrl=" + (ServerUrl ?? "-"),
            "deviceId=" + (DeviceId ?? "-"),
            "token=" + Secret(Token),
            "extraHeaderName=" + (ExtraHeaderName ?? "-"),
            "extraHeaderValue=" + Secret(ExtraHeaderValue),
            "requireTarget=" + Show(RequireTarget),
            "imageTypes=" + (ImageTypes is null ? "-" : string.Join("|", ImageTypes)),
            "thumbnailMode=" + Show(ThumbnailMode),
            "thumbnailLongEdgePx=" + Show(ThumbnailLongEdgePx),
            "thumbnailJpegQuality=" + Show(ThumbnailJpegQuality),
            "batchSize=" + Show(BatchSize),
            "htmlReport=" + Show(HtmlReport),
            "streamsEnabled=" + Show(StreamsEnabled),
            "streams=" + (Streams is null ? "-" : Streams.Count + " entries"),
        };
        return "BootstrapSettings(" + string.Join(", ", parts) + ")";
    }

    private static string Show<T>(T? value) where T : struct
        => value.HasValue ? value.Value.ToString() ?? "-" : "-";

    private static string Secret(string? value) => value is null ? "-" : "[redacted]";

    private static JsonDocument? TryParseObject(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            return document;
        }

        document.Dispose();
        return null;
    }

    /// <summary>Un valore e' valido se, applicato da solo su una configurazione neutra e valida, non produce errori.</summary>
    private static bool Valid(Action<UplinkSettings> change)
    {
        var settings = new UplinkSettings
        {
            ServerUrl = "https://bootstrap.invalid",
            DeviceId = "bootstrap",
        };
        change(settings);
        return SettingsValidator.Validate(settings).Count == 0;
    }

    private static bool IsToken(string token)
        => token.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c));

    private sealed class FieldReader
    {
        private readonly Dictionary<string, JsonElement> _values;
        private readonly List<string> _applied = new();
        private readonly List<string> _rejected = new();

        public FieldReader(Dictionary<string, JsonElement> values)
        {
            _values = values;
        }

        public IReadOnlyList<string> Applied => _applied;

        public IReadOnlyList<string> Rejected => _rejected;

        public bool? Bool(string field)
        {
            if (!_values.TryGetValue(field, out var element))
            {
                return null;
            }

            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                _applied.Add(field);
                return element.GetBoolean();
            }

            _rejected.Add(field);
            return null;
        }

        public string? Text(string field, Func<string, bool> isValid)
        {
            if (!_values.ContainsKey(field))
            {
                return null;
            }

            var text = RawText(field);
            if (text is not null && isValid(text))
            {
                _applied.Add(field);
                return text;
            }

            _rejected.Add(field);
            return null;
        }

        public int? Int(string field, Action<UplinkSettings, int> apply)
        {
            if (!_values.TryGetValue(field, out var element))
            {
                return null;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && Valid(s => apply(s, value)))
            {
                _applied.Add(field);
                return value;
            }

            _rejected.Add(field);
            return null;
        }

        public ThumbnailMode? Mode(string field)
        {
            if (!_values.TryGetValue(field, out var element))
            {
                return null;
            }

            var text = element.ValueKind == JsonValueKind.String ? element.GetString()?.Trim() : null;
            if (text is not null
                && Enum.TryParse<ThumbnailMode>(text, ignoreCase: true, out var mode)
                && Enum.IsDefined(mode))
            {
                _applied.Add(field);
                return mode;
            }

            _rejected.Add(field);
            return null;
        }

        public IReadOnlyList<string>? ImageTypes(string field)
        {
            if (!_values.TryGetValue(field, out var element))
            {
                return null;
            }

            var types = ReadImageTypes(element);
            if (types is not null)
            {
                _applied.Add(field);
                return types;
            }

            _rejected.Add(field);
            return null;
        }

        /// <summary>Nome e valore dell'header extra: sono validi solo insieme (regola di SettingsValidator).</summary>
        public (string? Name, string? Value) Header(string nameField, string valueField)
        {
            var hasName = _values.ContainsKey(nameField);
            var hasValue = _values.ContainsKey(valueField);
            if (!hasName && !hasValue)
            {
                return (null, null);
            }

            var name = RawText(nameField);
            var value = RawText(valueField);
            if (hasName && hasValue && name is not null && value is not null
                && !value.Any(char.IsControl)
                && Valid(s =>
                {
                    s.ExtraHeaderName = name;
                    s.ExtraHeaderValue = value;
                }))
            {
                _applied.Add(nameField);
                _applied.Add(valueField);
                return (name, value);
            }

            if (hasName)
            {
                _rejected.Add(nameField);
            }

            if (hasValue)
            {
                _rejected.Add(valueField);
            }

            return (null, null);
        }

        /// <summary>Lista di voci: se una voce non e' valida il campo intero viene scartato. Lista vuota e' valida.</summary>
        public IReadOnlyList<BootstrapStream>? Streams(string field)
        {
            if (!_values.TryGetValue(field, out var element))
            {
                return null;
            }

            var streams = ReadStreams(element);
            if (streams is not null)
            {
                _applied.Add(field);
                return streams;
            }

            _rejected.Add(field);
            return null;
        }

        private string? RawText(string field)
        {
            if (!_values.TryGetValue(field, out var element) || element.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = element.GetString()?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static IReadOnlyList<BootstrapStream>? ReadStreams(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > StreamsLimits.MaxEntries)
            {
                return null;
            }

            var list = new List<BootstrapStream>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !TryReadStream(item, out var stream))
                {
                    return null;
                }

                list.Add(stream);
            }

            return list;
        }

        private static bool TryReadStream(JsonElement item, out BootstrapStream stream)
        {
            stream = new BootstrapStream(string.Empty, null, string.Empty, true);

            var titleValue = Lookup(item, "title");
            var urlValue = Lookup(item, "url");
            var descriptionValue = Lookup(item, "description");
            var enabledValue = Lookup(item, "enabled");

            if (titleValue is not { ValueKind: JsonValueKind.String } || urlValue is not { ValueKind: JsonValueKind.String })
            {
                return false;
            }

            if (descriptionValue is { } d && d.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return false;
            }

            if (enabledValue is { } e && e.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            var title = titleValue.Value.GetString()?.Trim() ?? string.Empty;
            var url = urlValue.Value.GetString()?.Trim() ?? string.Empty;
            var description = descriptionValue is { ValueKind: JsonValueKind.String } dv ? dv.GetString() : null;
            if (StreamsValidator.ValidateFields(title, description, url).Count > 0)
            {
                return false;
            }

            var enabled = enabledValue is not { ValueKind: JsonValueKind.False };
            stream = new BootstrapStream(
                title,
                string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                url,
                enabled);
            return true;
        }

        private static JsonElement? Lookup(JsonElement item, string name)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }
            }

            return null;
        }

        private static IReadOnlyList<string>? ReadImageTypes(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var list = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var type = item.GetString()?.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(type) || !AllowedImageTypes.Contains(type))
                {
                    return null;
                }

                list.Add(type);
            }

            return list.Count == 0 ? null : FrameAdmission.NormalizeImageTypes(list);
        }
    }
}

/// <summary>Esito del parsing: impostazioni valide e nomi dei campi applicati, scartati o ignorati (mai valori).</summary>
public sealed record BootstrapParseResult(
    BootstrapSettings Settings,
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Rejected,
    IReadOnlyList<string> Ignored);
