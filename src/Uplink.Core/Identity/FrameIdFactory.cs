using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MyUniverseUplink.Core.Identity;

/// <summary>
/// frameId deterministico: primi 32 caratteri esadecimali minuscoli di SHA-256 di
/// "profileId|cameraName|capturedAtUtc (ISO-8601, ms, Z)|nome file senza estensione".
/// FITS e XISF dello stesso frame producono lo stesso id.
/// </summary>
public static class FrameIdFactory
{
    public const int IdLength = 32;

    public static string Create(string profileId, string cameraName, DateTimeOffset capturedAtUtc, string fileName)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(cameraName);
        ArgumentNullException.ThrowIfNull(fileName);

        var canonical = CanonicalString(profileId, cameraName, capturedAtUtc, fileName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash, 0, IdLength / 2).ToLowerInvariant();
    }

    /// <summary>Stringa canonica prima dell'hash. Esposta per i test e per il debug.</summary>
    public static string CanonicalString(string profileId, string cameraName, DateTimeOffset capturedAtUtc, string fileName)
    {
        var captured = capturedAtUtc.ToUniversalTime().ToString(UtcMillisFormat, CultureInfo.InvariantCulture);
        return $"{profileId}|{cameraName}|{captured}|{StripExtension(fileName)}";
    }

    internal const string UtcMillisFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Rimuove la directory e l'ultima estensione. "a.b.fits" diventa "a.b".</summary>
    public static string StripExtension(string fileName)
    {
        var name = fileName.Trim();
        var separator = name.LastIndexOfAny(['/', '\\']);
        if (separator >= 0)
        {
            name = name[(separator + 1)..];
        }

        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
