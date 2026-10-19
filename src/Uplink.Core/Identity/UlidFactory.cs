using System.Security.Cryptography;

namespace MyUniverseUplink.Core.Identity;

/// <summary>ULID (26 caratteri Crockford): 48 bit di tempo in ms + 80 bit casuali. Usato come batchId.</summary>
public static class UlidFactory
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewUlid(DateTimeOffset now)
    {
        var milliseconds = now.ToUnixTimeMilliseconds();
        Span<char> chars = stackalloc char[26];

        for (var i = 9; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(milliseconds & 31)];
            milliseconds >>= 5;
        }

        Span<byte> random = stackalloc byte[10];
        RandomNumberGenerator.Fill(random);

        var buffer = 0;
        var bits = 0;
        var index = 10;
        foreach (var b in random)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                chars[index++] = Alphabet[(buffer >> bits) & 31];
            }

            buffer &= (1 << bits) - 1;
        }

        return new string(chars);
    }
}
