using System.Security.Cryptography;
using System.Text;

namespace MyUniverseUplink.Settings;

/// <summary>Cifratura DPAPI (ambito CurrentUser) per token e valori segreti salvati nel profilo NINA.</summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MyUniverseUplink.secret.v1");

    public static string Protect(string plain)
    {
        ArgumentException.ThrowIfNullOrEmpty(plain);
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipher);
    }

    /// <summary>Restituisce null se il valore e' vuoto o non decifrabile (altro utente Windows, dato corrotto).</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
