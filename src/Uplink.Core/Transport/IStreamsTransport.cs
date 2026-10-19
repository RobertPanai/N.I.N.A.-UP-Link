using MyUniverseUplink.Core.Model;

namespace MyUniverseUplink.Core.Transport;

/// <summary>Invio dell'insieme delle dirette (PUT /api/uplink/streams). Implementato da UplinkClient.</summary>
public interface IStreamsTransport
{
    Task<UplinkResponse<StreamsResponse>> PutStreamsAsync(StreamsRequest request, CancellationToken cancellationToken);
}
