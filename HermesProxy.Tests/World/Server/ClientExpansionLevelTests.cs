using HermesProxy.Enums;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// The expansion level the proxy advertises to a modern Classic client must be 0-indexed
/// (Classic = 0, TBC = 1, WotLK = 2). <c>LegacyVersion.ExpansionVersion</c> is the 1-indexed
/// major; sending it verbatim told the client the realm was one expansion ahead, which zeroed
/// its quest/mob green range — every mob and quest below the player rendered grey.
/// </summary>
public class ClientExpansionLevelTests
{
    [Theory]
    [InlineData(ClientVersionBuild.V1_12_1_5875, 0)]  // vanilla backend -> 1.14 client
    [InlineData(ClientVersionBuild.V2_4_3_8606, 1)]   // TBC backend     -> 2.5 client
    [InlineData(ClientVersionBuild.V3_3_5a_12340, 2)] // WotLK backend   -> 3.4 client
    public void LegacyBackend_AdvertisesTheZeroIndexedExpansion(ClientVersionBuild legacy, byte expected)
        => Assert.Equal(expected, VersionChecker.GetClientExpansionLevel(legacy));
}
