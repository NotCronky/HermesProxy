using System.Reflection;
using HermesProxy.Enums;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A movie the server plays (a quest's, or a GM's .debug play movie) comes as SMSG_TRIGGER_MOVIE,
/// the movie id alone. The 3.4.3.54261 client reads it at 0x26CB (a 4-byte struct between
/// SMSG_TOTEM_MOVED 0x26CA and SMSG_SHOW_TAXI_NODES 0x26CD); the 3.4.3 table had no value for it
/// and the handler passed it on only from a 4.3.4 server, so the proxy dropped it.
/// </summary>
public sealed class TriggerMovieTests
{
    private static readonly PropertyInfo WorldPacketField =
        typeof(ServerPacket).GetProperty("_worldPacket", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Theory]
    [InlineData(ClientVersionBuild.V3_4_3_54261, 0x26CBu)]
    [InlineData(ClientVersionBuild.V3_3_5a_12340, 0x464u)]
    public void TriggerMovie_IsMapped(ClientVersionBuild build, uint expected)
        => Assert.Equal(expected, Opcodes.GetOpcodeValueForVersion(Opcode.SMSG_TRIGGER_MOVIE, build));

    [Fact]
    public void TriggerMovie_IsPassedOnWithItsId()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.Deliver(Opcode.SMSG_TRIGGER_MOVIE, [0, 0, 16, 0, 0, 0], harness.Client.HandleTriggerMovie);

        var movie = Assert.IsType<TriggerMovie>(Assert.Single(harness.ClientWire.Sent).Packet);
        movie.Write();
        Assert.Equal([16, 0, 0, 0], ((WorldPacket)WorldPacketField.GetValue(movie)!).GetData());
    }
}
