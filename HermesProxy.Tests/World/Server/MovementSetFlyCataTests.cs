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
/// Taking off or landing on a flying mount reaches a 4.3.4 server as CMSG_MOVE_SET_CAN_FLY (0x720E),
/// the opcode TrinityCore 4.3.4 reads it as. The client's CMSG_MOVE_SET_FLY has no MSG_ opcode to go
/// out as, and was sent as MSG_MOVE_SET_FACING.
/// </summary>
/// <remarks>
/// Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>); otherwise it returns at once.
/// </remarks>
public sealed class MovementSetFlyCataTests
{
    private static readonly WowGuid64 Player = new(HighGuidTypeLegacy.Player, 77);

    [Fact]
    public void SetFly_GoesOutAsSetCanFly()
    {
        if (!WorldClient.IsCataLegacy)
            return;
        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, socket: null, harness.Client);
        var movement = new ClientPlayerMovement { Guid = harness.SetActivePlayer(Player) };
        movement.MoveInfo.Flags = MovementFlagModern.CanFly | MovementFlagModern.Flying;

        MovementSystem.HandlePlayerMove(Opcode.CMSG_MOVE_SET_FLY, in movement, in ctx);

        Assert.Equal(0x720Eu, Assert.Single(harness.ServerWire.Sent).Opcode);
    }
}
