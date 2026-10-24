using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A world port ends every sail the proxy was driving. An arena's transports carry the same guid
/// in every instance of the map, so a Ring of Valor elevator the last match left raised would
/// otherwise meet the next match's create with that match's sail still on record: the create
/// reads as a flip back down, and the client would lower the elevator for 20 s from the top while
/// the server put the players on it at the bottom (#291).
/// </summary>
public sealed class TransportWorldPortTests
{
    private static readonly WowGuid64 Elevator = new(HighGuidTypeLegacy.Transport, 194582, 1);

    private static byte[] NewWorld(uint mapId)
        => LegacyPacketBuilder.Build(Opcode.SMSG_NEW_WORLD, packet =>
        {
            packet.WriteUInt32(mapId);
            packet.WriteFloat(763.5f);
            packet.WriteFloat(-294.5f);
            packet.WriteFloat(2.8f);
            packet.WriteFloat(3.14f);
        });

    [Fact]
    public void NewWorld_AfterAMatchRaisedTheElevator_NextCreateStartsParked()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater, "the transport sail is translated from 3.4.3 on");
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        var elevator = harness.AddKnownObject(Elevator, ObjectType.GameObject);
        var transports = harness.Session.GameState.SynthesizedTransports;
        // The last match raised it, long ago.
        transports[elevator] = new SynthesizedTransport(20133, Vector3.Zero, 0,
            SailDeadline: 1000, SailTargetState: 25);

        harness.Session.GameState.IsWaitingForNewWorld = true;
        harness.Deliver(Opcode.SMSG_NEW_WORLD, NewWorld(618), harness.Client.HandleNewWorld);

        Assert.Empty(transports);

        // The next match's create: AzerothCore has the elevator lowered (GO_STATE_READY) and
        // still publishes its path fraction pinned at the far end.
        uint before = Time.GetMSTime();
        var create = new ObjectUpdate(elevator, UpdateTypeModern.CreateObject1, harness.Session);
        create.CreateData.MoveInfo = new MovementInfo();
        create.GameObjectData.TypeID = (sbyte)GameObjectTypeModern.Transport;
        create.GameObjectData.Level = 20133;
        create.GameObjectData.State = 1;
        create.ObjectData.DynamicFlags = 0xFFFF0000;
        create.ObjectData.EntryID = 194582;
        create.InitializePlaceholders();
        uint after = Time.GetMSTime();

        Assert.Equal((sbyte)24, create.GameObjectData.State);
        Assert.InRange((uint)create.GameObjectData.Level!.Value, before, after);
    }
}
