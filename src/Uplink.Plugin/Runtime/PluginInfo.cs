namespace MyUniverseUplink.Runtime;

/// <summary>Versioni note a runtime: plugin (assembly) e NINA (CoreUtil.Version).</summary>
public static class PluginInfo
{
    public static string PluginVersion { get; } = typeof(PluginInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string NinaVersion { get; } = ReadNinaVersion();

    private static string ReadNinaVersion()
    {
        try
        {
            return NINA.Core.Utility.CoreUtil.Version ?? "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
