using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// AzerothCore sends MSG_MOVE_HEARTBEAT flagged MOVEFLAG_SPLINE_ENABLED, about twice a second, for
/// a player it moves along a spline (every playerbot). Forwarded as a MoveUpdate, the modern client
/// drops the spline it is following and snaps the unit to the heartbeat: bots rubber-band.
/// </summary>
public class SplineHeartbeatTests
{
    private readonly LegacyHandlerHarness _harness = new(recordClientPackets: true);
    private readonly WowGuid64 _legacyPlayer = new(HighGuidTypeLegacy.Player, 1);
    private readonly WowGuid64 _legacyBot = new(HighGuidTypeLegacy.Player, 2);

    public SplineHeartbeatTests()
    {
        _harness.SetActivePlayer(_legacyPlayer);
        _harness.AddKnownObject(_legacyBot, ObjectType.Player);
    }

    private void DeliverHeartbeat(WowGuid64 mover, MovementFlagWotLK flags)
    {
        byte[] wire = LegacyPacketBuilder.Build(Opcode.MSG_MOVE_HEARTBEAT, packet =>
        {
            packet.WritePackedGuid(mover);
            packet.WriteUInt32((uint)flags);
            packet.WriteUInt16(0);          // extra flags
            packet.WriteUInt32(1291428);    // move time
            packet.WriteFloat(1197.1f);
            packet.WriteFloat(1472.8f);
            packet.WriteFloat(306.2f);
            packet.WriteFloat(3.1f);        // orientation
            packet.WriteUInt32(0);          // fall time
        });
        _harness.Deliver(Opcode.MSG_MOVE_HEARTBEAT, wire, _harness.Client.HandleMovementMessages);
    }

    [Fact]
    public void Heartbeat_ForOtherUnitOnSpline_IsNotForwarded()
    {
        DeliverHeartbeat(_legacyBot, MovementFlagWotLK.Forward | MovementFlagWotLK.SplineEnabled);

        Assert.DoesNotContain(_harness.ClientWire.Sent, s => s.Packet is MoveUpdate);
    }

    [Fact]
    public void Heartbeat_ForOtherUnitWithoutSpline_IsForwarded()
    {
        DeliverHeartbeat(_legacyBot, MovementFlagWotLK.Forward);

        var update = Assert.IsType<MoveUpdate>(Assert.Single(_harness.ClientWire.Sent).Packet);
        Assert.Equal(_legacyBot.To128(_harness.Session.GameState), update.MoverGUID);
    }

    [Fact]
    public void Heartbeat_ForOwnPlayerOnSpline_IsForwarded()
    {
        DeliverHeartbeat(_legacyPlayer, MovementFlagWotLK.Forward | MovementFlagWotLK.SplineEnabled);

        Assert.Single(_harness.ClientWire.Sent.Where(s => s.Packet is MoveUpdate));
    }
}
