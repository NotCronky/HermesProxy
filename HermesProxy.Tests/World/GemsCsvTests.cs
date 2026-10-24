using System.IO;

using HermesProxy.World;

using Xunit;

namespace HermesProxy.Tests.World;

/// <summary>
/// CSV/Gems*.csv names the gem item a server's socket enchantment stands for, which the client is
/// sent in place of the enchantment. A gem the file lacks is shown as an empty socket. These tests
/// read the shipped files through the loader's own parser.
/// </summary>
public sealed class GemsCsvTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ShippedTable_Loads(int expansion)
    {
        // ReadGems adds into a dictionary: a repeated enchantment throws, at startup.
        Assert.NotEmpty(GameData.ReadGems(Path.Combine("CSV", $"Gems{expansion}.csv")));
    }

    [Fact]
    public void ForTheCataclysmClient_ACataclysmGem_IsNamed()
    {
        var gems = GameData.ReadGems(Path.Combine("CSV", "Gems4.csv"));

        // Brilliant Inferno Ruby, as a 4.3.4 server names it in a socket
        Assert.Equal(52207u, gems[3995]);
    }

    [Fact]
    public void ForTheCataclysmClient_TheWrathGems_AreKept()
    {
        var wrath = GameData.ReadGems(Path.Combine("CSV", "Gems3.csv"));
        var cataclysm = GameData.ReadGems(Path.Combine("CSV", "Gems4.csv"));

        foreach (var (enchant, item) in cataclysm)
        {
            if (wrath.TryGetValue(enchant, out uint wrathItem))
                Assert.Equal(wrathItem, item);
        }
    }
}
