using System.Text;
using System.Text.Json;
using MyUniverseUplink.Core.Model;

namespace MyUniverseUplink.Core.Streams;

/// <summary>Ultimo insieme pubblicato con successo: impronta, conteggi e istante. Nessun URL, nessun segreto.</summary>
public sealed record StreamsSyncState(string? Fingerprint, int Stored, int Rejected, bool? Visible, DateTimeOffset? SyncedAtUtc)
{
    public static StreamsSyncState Empty { get; } = new(null, 0, 0, null, null);
}

public interface IStreamsStateStore
{
    StreamsSyncState Load();

    void Save(StreamsSyncState state);
}

/// <summary>File JSON nella cartella del plugin. Errori di lettura o scrittura non lanciano: lo stato resta quello precedente.</summary>
public sealed class FileStreamsStateStore : IStreamsStateStore
{
    private readonly string _path;

    public FileStreamsStateStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public StreamsSyncState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return StreamsSyncState.Empty;
            }

            var dto = JsonSerializer.Deserialize<StateDto>(File.ReadAllText(_path, Encoding.UTF8), UplinkJson.Options);
            return dto is null
                ? StreamsSyncState.Empty
                : new StreamsSyncState(dto.Fingerprint, dto.Stored, dto.Rejected, dto.Visible, dto.SyncedAtUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return StreamsSyncState.Empty;
        }
    }

    public void Save(StreamsSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            var dto = new StateDto
            {
                Fingerprint = state.Fingerprint,
                Stored = state.Stored,
                Rejected = state.Rejected,
                Visible = state.Visible,
                SyncedAtUtc = state.SyncedAtUtc,
            };
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(dto, UplinkJson.Options), new UTF8Encoding(false));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Senza stato salvato, al prossimo avvio si reinvia: nessun dato perso.
        }
    }

    private sealed class StateDto
    {
        public string? Fingerprint { get; set; }

        public int Stored { get; set; }

        public int Rejected { get; set; }

        public bool? Visible { get; set; }

        public DateTimeOffset? SyncedAtUtc { get; set; }
    }
}
