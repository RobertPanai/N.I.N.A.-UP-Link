namespace MyUniverseUplink.Core.Activity;

/// <summary>Punto di pubblicazione degli eventi. Chi pubblica (handler di ImageSaved compreso) non deve attendere I/O.</summary>
public interface IActivitySink
{
    void Publish(ActivitySeverity severity, string category, string message);
}
