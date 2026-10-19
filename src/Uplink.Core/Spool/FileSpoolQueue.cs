using System.Text;
using System.Text.Json;
using MyUniverseUplink.Core.Model;

namespace MyUniverseUplink.Core.Spool;

/// <summary>
/// Coda su disco: pending\{frameId}.json e dead\{frameId}.json (+ dead\{frameId}.reason.txt).
/// Scritture atomiche (file temporaneo + flush + File.Move). Operazioni sincrone e veloci, serializzate da un lock.
/// ReadBatch ordina per data di accodamento (FIFO) e non legge il contenuto oltre il batch richiesto.
/// </summary>
public sealed class FileSpoolQueue
{
    private const string PendingDirName = "pending";
    private const string DeadDirName = "dead";
    private const string JsonExtension = ".json";
    private const string ReasonSuffix = ".reason.txt";
    private const int MaxIdLength = 128;

    private readonly object _gate = new();
    private readonly string _pendingDir;
    private readonly string _deadDir;

    public FileSpoolQueue(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        _pendingDir = Path.Combine(rootDirectory, PendingDirName);
        _deadDir = Path.Combine(rootDirectory, DeadDirName);
        Directory.CreateDirectory(_pendingDir);
        Directory.CreateDirectory(_deadDir);

        lock (_gate)
        {
            DeleteOrphanTempFiles(_pendingDir);
            DeleteOrphanTempFiles(_deadDir);
        }
    }

    public string PendingDirectory => _pendingDir;

    public string DeadDirectory => _deadDir;

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return CountJson(_pendingDir);
            }
        }
    }

    public int DeadCount
    {
        get
        {
            lock (_gate)
            {
                return CountJson(_deadDir);
            }
        }
    }

    /// <summary>Accoda o sostituisce il frame (idempotente sull'id). Restituisce quando il file e' su disco.</summary>
    public void Enqueue(FrameRecord frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        RequireSafeId(frame.FrameId);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, UplinkJson.Options);
        lock (_gate)
        {
            WriteAtomic(PendingPath(frame.FrameId), bytes);
        }
    }

    /// <summary>
    /// Primi <paramref name="maxCount"/> frame in attesa, in ordine di accodamento. I file corrotti o incoerenti
    /// finiscono in dead con il motivo, senza bloccare il resto della coda.
    /// </summary>
    public IReadOnlyList<FrameRecord> ReadBatch(int maxCount)
    {
        if (maxCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Il batch deve contenere almeno un frame.");
        }

        lock (_gate)
        {
            var files = new DirectoryInfo(_pendingDir)
                .EnumerateFiles("*" + JsonExtension)
                .OrderBy(f => f.LastWriteTimeUtc)
                .ThenBy(f => f.Name, StringComparer.Ordinal)
                .ToList();

            var result = new List<FrameRecord>(Math.Min(maxCount, files.Count));
            foreach (var file in files)
            {
                if (result.Count >= maxCount)
                {
                    break;
                }

                var frame = TryLoad(file.FullName, Path.GetFileNameWithoutExtension(file.Name));
                if (frame is not null)
                {
                    result.Add(frame);
                }
            }

            return result;
        }
    }

    /// <summary>Rimuove dalla coda i frame confermati dal server (stored o duplicate).</summary>
    public void Complete(IEnumerable<string> frameIds)
    {
        ArgumentNullException.ThrowIfNull(frameIds);
        lock (_gate)
        {
            foreach (var id in frameIds)
            {
                RequireSafeId(id);
                File.Delete(PendingPath(id));
            }
        }
    }

    /// <summary>Sposta un frame in dead con il motivo. Restituisce false se il frame non e' in pending.</summary>
    public bool MoveToDead(string frameId, string reason)
    {
        RequireSafeId(frameId);
        lock (_gate)
        {
            var source = PendingPath(frameId);
            if (!File.Exists(source))
            {
                return false;
            }

            File.Move(source, DeadPath(frameId), overwrite: true);
            WriteAtomic(ReasonPath(frameId), Encoding.UTF8.GetBytes(reason ?? string.Empty));
            return true;
        }
    }

    /// <summary>Riporta un frame da dead a pending. Restituisce false se non esiste in dead.</summary>
    public bool Requeue(string frameId)
    {
        RequireSafeId(frameId);
        lock (_gate)
        {
            var source = DeadPath(frameId);
            if (!File.Exists(source))
            {
                return false;
            }

            File.Move(source, PendingPath(frameId), overwrite: true);
            File.Delete(ReasonPath(frameId));
            return true;
        }
    }

    /// <summary>Riporta tutti i frame da dead a pending. Restituisce quanti ne ha riaccodati.</summary>
    public int RequeueAll()
    {
        List<string> ids;
        lock (_gate)
        {
            ids = new DirectoryInfo(_deadDir)
                .EnumerateFiles("*" + JsonExtension)
                .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                .ToList();
        }

        return ids.Count(Requeue);
    }

    /// <summary>Elenco dei frame in dead con motivo e istante di spostamento (per UI e report).</summary>
    public IReadOnlyList<DeadLetter> ListDead()
    {
        lock (_gate)
        {
            var result = new List<DeadLetter>();
            foreach (var file in new DirectoryInfo(_deadDir).EnumerateFiles("*" + JsonExtension))
            {
                var id = Path.GetFileNameWithoutExtension(file.Name);
                var reasonPath = ReasonPath(id);
                var reason = File.Exists(reasonPath) ? File.ReadAllText(reasonPath, Encoding.UTF8) : string.Empty;
                result.Add(new DeadLetter(id, reason, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
            }

            return result;
        }
    }

    private FrameRecord? TryLoad(string path, string expectedId)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            // File occupato o in scrittura da un altro processo: non e' corrotto, lo riprendiamo al giro dopo.
            return null;
        }

        FrameRecord? frame;
        try
        {
            frame = JsonSerializer.Deserialize<FrameRecord>(bytes, UplinkJson.Options);
        }
        catch (JsonException)
        {
            MoveCorruptToDead(expectedId, "corrupt JSON in pending spool file");
            return null;
        }

        if (frame is null || !string.Equals(frame.FrameId, expectedId, StringComparison.Ordinal))
        {
            MoveCorruptToDead(expectedId, "frameId does not match file name");
            return null;
        }

        return frame;
    }

    private void MoveCorruptToDead(string frameId, string reason)
    {
        var source = PendingPath(frameId);
        if (File.Exists(source))
        {
            File.Move(source, DeadPath(frameId), overwrite: true);
            WriteAtomic(ReasonPath(frameId), Encoding.UTF8.GetBytes(reason));
        }
    }

    private void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private string PendingPath(string frameId) => Path.Combine(_pendingDir, frameId + JsonExtension);

    private string DeadPath(string frameId) => Path.Combine(_deadDir, frameId + JsonExtension);

    private string ReasonPath(string frameId) => Path.Combine(_deadDir, frameId + ReasonSuffix);

    private static void RequireSafeId(string frameId)
    {
        if (string.IsNullOrEmpty(frameId) || frameId.Length > MaxIdLength
            || !frameId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
        {
            throw new ArgumentException("frameId non valido per il nome file dello spool.", nameof(frameId));
        }
    }

    private static int CountJson(string directory)
        => new DirectoryInfo(directory).EnumerateFiles("*" + JsonExtension).Count();

    private static void DeleteOrphanTempFiles(string directory)
    {
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.tmp"))
        {
            TryDelete(file.FullName);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Nulla da fare: il file temporaneo verra' ripulito al prossimo avvio.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem.
        }
    }
}

public sealed record DeadLetter(string FrameId, string Reason, DateTimeOffset MovedAtUtc);
