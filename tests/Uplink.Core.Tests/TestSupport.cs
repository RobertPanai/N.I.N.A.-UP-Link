using System.Text.Json;

namespace MyUniverseUplink.Core.Tests;

internal static class RepoPaths
{
    /// <summary>Risale dalla cartella di output dei test fino alla root del repo (quella che contiene contract/).</summary>
    public static string Root
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 10 && !Directory.Exists(Path.Combine(dir, "contract")); i++)
            {
                dir = Path.GetFullPath(Path.Combine(dir, ".."));
            }

            return dir;
        }
    }

    public static string Contract(string fileName) => Path.Combine(Root, "contract", fileName);
}

/// <summary>Confronto strutturale di JSON: stesse chiavi, stessi valori; i numeri si confrontano come double (90.0 == 90).</summary>
internal static class JsonEquivalence
{
    public static bool AreEquivalent(JsonElement a, JsonElement b, out string difference)
    {
        difference = string.Empty;
        return Compare(a, b, "$", ref difference);
    }

    private static bool Compare(JsonElement a, JsonElement b, string path, ref string difference)
    {
        if (a.ValueKind != b.ValueKind)
        {
            difference = $"{path}: tipo {a.ValueKind} vs {b.ValueKind}";
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var propsA = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var propsB = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                foreach (var key in propsA.Keys.Union(propsB.Keys))
                {
                    if (!propsA.TryGetValue(key, out var va) || !propsB.TryGetValue(key, out var vb))
                    {
                        difference = $"{path}.{key}: presente solo in un lato";
                        return false;
                    }

                    if (!Compare(va, vb, $"{path}.{key}", ref difference))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.Array:
                var itemsA = a.EnumerateArray().ToList();
                var itemsB = b.EnumerateArray().ToList();
                if (itemsA.Count != itemsB.Count)
                {
                    difference = $"{path}: lunghezza {itemsA.Count} vs {itemsB.Count}";
                    return false;
                }

                for (var i = 0; i < itemsA.Count; i++)
                {
                    if (!Compare(itemsA[i], itemsB[i], $"{path}[{i}]", ref difference))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.Number:
                if (Math.Abs(a.GetDouble() - b.GetDouble()) < 1e-9)
                {
                    return true;
                }

                difference = $"{path}: {a.GetRawText()} vs {b.GetRawText()}";
                return false;

            case JsonValueKind.String:
                if (a.GetString() == b.GetString())
                {
                    return true;
                }

                difference = $"{path}: '{a.GetString()}' vs '{b.GetString()}'";
                return false;

            default:
                return true;
        }
    }
}
