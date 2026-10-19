using System.IO;
using System.Text;

namespace MyUniverseUplink.Services;

/// <summary>Log su file in logs\uplink.log con rotazione a 1 MB e 5 file di storico. Non lancia mai eccezioni.</summary>
public sealed class FileLog
{
    private const long MaxBytes = 1_000_000;
    private const int KeepRotated = 5;
    private const string FileName = "uplink.log";

    private readonly string _directory;
    private readonly object _gate = new();

    public FileLog(string directory)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public void Write(string level, string message)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, FileName);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    Rotate(path);
                }

                File.AppendAllText(path, $"{DateTime.UtcNow:O} {level,-5} {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception)
            {
                // Il log su file non deve mai far fallire il plugin.
            }
        }
    }

    private void Rotate(string path)
    {
        for (var i = KeepRotated - 1; i >= 1; i--)
        {
            var from = Path.Combine(_directory, $"uplink.{i}.log");
            if (File.Exists(from))
            {
                File.Move(from, Path.Combine(_directory, $"uplink.{i + 1}.log"), overwrite: true);
            }
        }

        File.Move(path, Path.Combine(_directory, "uplink.1.log"), overwrite: true);
    }
}
