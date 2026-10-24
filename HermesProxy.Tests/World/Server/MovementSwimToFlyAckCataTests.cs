using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// The client's ack of a swim-to-fly change reaches a 4.3.4 server as 0x3014, which TrinityCore 4.3.4
/// keeps the change pending for. It had no handler and was dropped. A 3.3.5a server never sends the
/// change, so there it is still not forwarded.
/// </summary>
public sealed class MovementSwimToFlyAckCataTests
{
    private static readonly WowGuid64 Player = new(HighGuidTypeLegacy.Player, 77);

    [Fact]
    public void Ack_GoesOutOnlyToA434Server()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, socket: null, harness.Client);
        var ack = new MovementAckMessage(harness.SetActivePlayer(Player),
            new MovementAck { MoveInfo = new MovementInfo { Flags = MovementFlagModern.CanFly }, MoveCounter = 3 });

        MovementSystem.HandleMoveEnableSwimToFlyTransAck(in ack, in ctx);

        if (WorldClient.IsCataLegacy)
            Assert.Equal(0x3014u, Assert.Single(harness.ServerWire.Sent).Opcode);
        else
            Assert.Empty(harness.ServerWire.Sent);
    }
}
