using System.Text.RegularExpressions;
using MyUniverseUplink.Runtime;
using NINA.Core.Utility;

namespace MyUniverseUplink.Services;

/// <summary>Implementazione di IUplinkLog: scrive nel log di NINA (Logger) e nel file del plugin, oscurando i segreti.</summary>
public sealed class NinaLogBridge : IUplinkLog
{
    private static readonly Regex BearerPattern = new(@"Bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly FileLog _file;
    private readonly object _gate = new();
    private readonly List<string> _secrets = new();

    public NinaLogBridge(FileLog file)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
    }

    private enum Level
    {
        Info,
        Warn,
        Error,
    }

    public void Info(string message) => Emit(Level.Info, message, null);

    public void Warn(string message) => Emit(Level.Warn, message, null);

    public void Error(string message, Exception? exception = null) => Emit(Level.Error, message, exception);

    public void RegisterSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 4)
        {
            return;
        }

        lock (_gate)
        {
            if (!_secrets.Contains(secret))
            {
                _secrets.Add(secret);
            }
        }
    }

    private void Emit(Level level, string message, Exception? exception)
    {
        try
        {
            var text = Redact(message);
            if (exception is not null)
            {
                text += " | " + exception.GetType().Name + ": " + Redact(exception.Message);
            }

            _file.Write(level.ToString().ToUpperInvariant(), text);

            switch (level)
            {
                case Level.Info:
                    Logger.Info(text);
                    break;
                case Level.Warn:
                    Logger.Warning(text);
                    break;
                default:
                    Logger.Error(text);
                    break;
            }
        }
        catch (Exception)
        {
            // Il logging non deve mai far fallire il plugin.
        }
    }

    private string Redact(string text)
    {
        lock (_gate)
        {
            foreach (var secret in _secrets)
            {
                text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
            }
        }

        return BearerPattern.Replace(text, "Bearer [redacted]");
    }
}
