using System.Security.Cryptography;
using System.Text;
using MyUniverseUplink.Core.Model;
using MyUniverseUplink.Core.Settings;

namespace MyUniverseUplink.Core.Streams;

public static class StreamsRequestFactory
{
    /// <summary>Costruisce la richiesta: solo voci abilitate, nell'ordine di lista. Con interruttore spento la lista e' vuota.</summary>
    public static StreamsRequest Build(DeviceInfo device, StreamsSettings settings, string requestId)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings);

        var streams = new List<StreamEntry>();
        if (settings.Enabled)
        {
            foreach (var item in settings.Items.Where(i => i.Enabled))
            {
                streams.Add(new StreamEntry
                {
                    StreamId = item.StreamId.Trim(),
                    Title = item.Title.Trim(),
                    Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
                    Url = item.Url.Trim(),
                    Order = streams.Count,
                });
            }
        }

        return new StreamsRequest
        {
            RequestId = requestId,
            Device = device,
            StreamsEnabled = settings.Enabled,
            Streams = streams,
        };
    }

    /// <summary>Impronta dell'insieme (server, dispositivo, interruttore, voci). Stesso insieme, stessa impronta.</summary>
    public static string Fingerprint(string serverKey, StreamsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = System.Text.Json.JsonSerializer.Serialize(
            new FingerprintInput(serverKey ?? string.Empty, request.Device.DeviceId, request.StreamsEnabled, request.Streams),
            Model.UplinkJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed record FingerprintInput(string Server, string Device, bool Enabled, List<StreamEntry> Streams);
}
