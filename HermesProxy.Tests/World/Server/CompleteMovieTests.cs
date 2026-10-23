using HermesProxy.Enums;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// The client tells the server a movie is over with CMSG_COMPLETE_MOVIE, an empty packet. The
/// 3.4.3.54261 client sends it as 0x34DD the moment a movie ends; the 3.4.3 table had no value
/// for it, and the handler told only a 4.3.4 server, so a 3.3.5a server never heard of it.
/// </summary>
public sealed class CompleteMovieTests
{
    [Theory]
    [InlineData(ClientVersionBuild.V3_4_3_54261, 0x34DDu)]
    [InlineData(ClientVersionBuild.V3_3_5a_12340, 0x465u)]
    public void CompleteMovie_IsMapped(ClientVersionBuild build, uint expected)
        => Assert.Equal(expected, Opcodes.GetOpcodeValueForVersion(Opcode.CMSG_COMPLETE_MOVIE, build));

    [Fact]
    public void CompleteMovie_ReachesTheServer()
    {
        Assert.SkipUnless(LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056), "servers before 3.0.2 play no movies");
        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, null, harness.Client);

        MiscSystem.HandleCompleteMovie(new EmptyClientPacket(), in ctx);

        var sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(LegacyVersion.GetCurrentOpcode(Opcode.CMSG_COMPLETE_MOVIE), sent.Opcode);
    }
}
