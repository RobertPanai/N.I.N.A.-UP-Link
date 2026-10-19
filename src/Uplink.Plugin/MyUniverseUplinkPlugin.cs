using System.ComponentModel.Composition;
using NINA.Plugin;
using NINA.Plugin.Interfaces;

namespace MyUniverseUplink;

/// <summary>Scheletro M0: plugin vuoto, visibile in NINA. La logica arriva in M2.</summary>
[Export(typeof(IPluginManifest))]
public sealed class MyUniverseUplinkPlugin : PluginBase
{
}
