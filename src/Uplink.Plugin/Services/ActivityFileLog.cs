using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;
using MyUniverseUplink.Core.Activity;

namespace MyUniverseUplink.Services;

/// <summary>
/// Copia ogni evento del feed in logs\activity.log: una riga per evento, ora locale. Rotazione a 1 MB con un solo
/// file storico (activity.1.log). La scrittura avviene in un thread di lavoro: pubblicare non attende il disco.
/// Non lancia mai eccezioni verso il chiamante.
/// </summary>
public sealed class ActivityFileLog : IDisposable
{
    private const long MaxBytes = 1_000_000;
    private const string FileName = "activity.log";
    private const string RotatedName = "activity.1.log";

    private readonly string _directory;
    private readonly ActivityFeed _feed;
    private readonly Channel<ActivityEntry> _queue = Channel.CreateUnbounded<ActivityEntry>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;
    private bool _disposed;

    public ActivityFileLog(string directory, ActivityFeed feed)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _feed.EntryAdded += OnEntryAdded;
        _writer = Task.Run(WriteLoopAsync);
    }

    private void OnEntryAdded(object? sender, ActivityEntry entry) => _queue.Writer.TryWrite(entry);

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                WriteLine(entry);
            }
        }
        catch (Exception)
        {
            // Il log delle attivita' non deve mai far fallire il plugin.
        }
    }

    private void WriteLine(ActivityEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, FileName);
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            {
                File.Move(path, Path.Combine(_directory, RotatedName), overwrite: true);
            }

            var local = entry.UtcTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var line = $"{local} {entry.Severity.Label()} {entry.Category,-8} {entry.Message}{Environment.NewLine}";
            File.AppendAllText(path, line, Encoding.UTF8);
        }
        catch (Exception)
        {
            // Nessuna eccezione verso il chiamante.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _feed.EntryAdded -= OnEntryAdded;
        _queue.Writer.TryComplete();
        try
        {
            _writer.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Lo scrittore non puo' lanciare: il wait serve solo a svuotare la coda in chiusura.
        }
    }
}
