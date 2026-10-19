using MyUniverseUplink.Core;
using Xunit;

namespace MyUniverseUplink.Core.Tests;

public class UplinkInfoTests
{
    [Fact]
    public void SchemaVersionIsOne() => Assert.Equal(1, UplinkInfo.ContractSchemaVersion);

    [Fact]
    public void ContractFixturesExist()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !Directory.Exists(Path.Combine(dir, "contract")); i++)
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        Assert.True(File.Exists(Path.Combine(dir, "contract", "batch-v1.request.json")), "fixture di contratto mancante");
    }
}
