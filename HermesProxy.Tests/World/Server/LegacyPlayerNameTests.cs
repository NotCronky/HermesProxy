using System;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// Legacy cores look a player name up by its whole string, so the realm suffix the modern
/// client appends has to be cut off before the name is forwarded (issue #335).
/// </summary>
public class LegacyPlayerNameTests
{
    [Theory]
    [InlineData("Brannoch-", "Brannoch")]              // unresolved realm, as captured in #335
    [InlineData("Brannoch-Duskhollow", "Brannoch")]
    [InlineData("Brannoch-Dusk-Hollow", "Brannoch")]
    [InlineData("Brannoch", "Brannoch")]
    [InlineData("龙骑士-Duskhollow", "龙骑士")]
    [InlineData("", "")]
    public void StripRealmSuffix_CutsAtFirstHyphen(string name, string expected)
    {
        Assert.Equal(expected, LegacyPlayerName.StripRealmSuffix(name).ToString());
        Assert.Equal(expected, LegacyPlayerName.StripRealmSuffixToString(name));
    }

    [Theory]
    [InlineData("Brannoch")]
    [InlineData("Brannoch-Duskhollow")]
    public void StripRealmSuffix_SlicesTheOriginalString(string name)
    {
        ReadOnlySpan<char> bare = LegacyPlayerName.StripRealmSuffix(name);

        Assert.True(bare.Overlaps(name.AsSpan(), out int offset));
        Assert.Equal(0, offset);
    }

    [Fact]
    public void StripRealmSuffixToString_NoHyphen_ReturnsSameInstance()
    {
        const string name = "Brannoch";
        Assert.Same(name, LegacyPlayerName.StripRealmSuffixToString(name));
    }
}
