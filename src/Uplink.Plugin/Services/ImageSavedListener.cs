using MyUniverseUplink.Core.Activity;
using MyUniverseUplink.Runtime;
using NINA.WPF.Base.Interfaces.Mediator;

namespace MyUniverseUplink.Services;

/// <summary>Si iscrive a ImageSaved del mediatore. Il gestore e' protetto: nessuna eccezione torna a NINA.</summary>
public sealed class ImageSavedListener
{
    private readonly IImageSaveMediator _mediator;
    private readonly Action<ImageSavedEventArgs> _onImage;
    private readonly IUplinkLog _log;
    private readonly IActivitySink? _activity;
    private bool _subscribed;

    public ImageSavedListener(IImageSaveMediator mediator, Action<ImageSavedEventArgs> onImage, IUplinkLog log, IActivitySink? activity = null)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _onImage = onImage ?? throw new ArgumentNullException(nameof(onImage));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _activity = activity;
    }

    public void Start()
    {
        if (_subscribed)
        {
            return;
        }

        _mediator.ImageSaved += OnImageSaved;
        _subscribed = true;
    }

    public void Stop()
    {
        if (!_subscribed)
        {
            return;
        }

        _mediator.ImageSaved -= OnImageSaved;
        _subscribed = false;
    }

    private void OnImageSaved(object? sender, ImageSavedEventArgs e)
    {
        try
        {
            _onImage(e);
        }
        catch (Exception ex)
        {
            _log.Error("ImageSaved handler failed; frame skipped", ex);
            _activity?.Publish(ActivitySeverity.Error, "frame", "handler failed (" + ex.GetType().Name + "); frame skipped");
        }
    }
}
