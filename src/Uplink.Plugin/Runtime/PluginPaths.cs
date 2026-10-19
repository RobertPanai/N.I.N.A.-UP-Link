using System.IO;
namespace MyUniverseUplink.Runtime;

/// <summary>Cartelle dati del plugin sotto %LOCALAPPDATA%\NINA\Plugins\MyUniverseUplink.</summary>
public sealed class PluginPaths
{
    public PluginPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        Pending = Path.Combine(Root, "pending");
        Dead = Path.Combine(Root, "dead");
        Thumbs = Path.Combine(Root, "thumbs");
        Logs = Path.Combine(Root, "logs");
        Report = Path.Combine(Root, "report");
    }

    public static PluginPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Plugins", "MyUniverseUplink"));

    public string Root { get; }

    public string Pending { get; }

    public string Dead { get; }

    public string Thumbs { get; }

    public string Logs { get; }

    public string Report { get; }

    public string ReportFile => Path.Combine(Report, "uplink-report.html");

    public void EnsureCreated()
    {
        foreach (var directory in new[] { Root, Pending, Dead, Thumbs, Logs, Report })
        {
            Directory.CreateDirectory(directory);
        }
    }
}
