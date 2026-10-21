using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// A change of transport, such as the end of the jump off a vehicle seat, reaches a 4.3.4 server as
/// CMSG_MOVE_CHNG_TRANSPORT (0x3102), the opcode TrinityCore 4.3.4 reads it as. The client's
/// CMSG_MOVE_CHANGE_TRANSPORT has no MSG_ opcode to go out as, and was sent as MSG_MOVE_SET_FACING.
/// </summary>
/// <remarks>
/// Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>); otherwise it returns at once.
/// </remarks>
public sealed class MovementChangeTransportCataTests
{
    private static readonly WowGuid64 Player = new(HighGuidTypeLegacy.Player, 77);

    [Fact]
    public void ChangeTransport_GoesOutAsItself()
    {
        if (!WorldClient.IsCataLegacy)
            return;
        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, socket: null, harness.Client);
        var movement = new ClientPlayerMovement { Guid = harness.SetActivePlayer(Player) };

        MovementSystem.HandlePlayerMove(Opcode.CMSG_MOVE_CHANGE_TRANSPORT, in movement, in ctx);

        Assert.Equal(0x3102u, Assert.Single(harness.ServerWire.Sent).Opcode);
    }
}
