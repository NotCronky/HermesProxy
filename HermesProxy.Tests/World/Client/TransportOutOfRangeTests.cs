using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// An OUT_OF_RANGE for a type 11 boat whose sail the proxy drives. TrinityCore never moves its
/// boat server-side and once dropped it out of range mid-crossing, under its rider, so that one is
/// kept. AzerothCore moves its boats itself (HighGuid::Transport) and removes the previous round's
/// pair at a Strand of the Ancients round change with exactly this block; kept, the old boat stayed
/// docked under the new one for the whole round.
/// </summary>
public sealed class TransportOutOfRangeTests
{
    private static readonly WowGuid64 AzerothCoreBoat = new(HighGuidTypeLegacy.Transport, 193182, 7);
    private static readonly WowGuid64 TrinityCoreBoat = new(HighGuidTypeLegacy.GameObject, 193182, 8);

    private static byte[] OutOfRange(params WowGuid64[] guids)
        => LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            packet.WriteUInt32(1);
            packet.WriteUInt8((byte)UpdateTypeLegacy.FarObjects);
            packet.WriteInt32(guids.Length);
            foreach (var guid in guids)
                packet.WritePackedGuid(guid);
        });

    [Fact]
    public void AzerothCoreBoat_OutOfRange_ReachesTheClient_TrinityCoreBoat_IsKept()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater, "the boat sail is translated from 3.4.3 on");
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        var transports = harness.Session.GameState.SynthesizedTransports;
        var acBoat = harness.AddKnownObject(AzerothCoreBoat, ObjectType.GameObject);
        var tcBoat = harness.AddKnownObject(TrinityCoreBoat, ObjectType.GameObject);
        transports[acBoat] = new SynthesizedTransport(60133, Vector3.Zero, 0);
        transports[tcBoat] = new SynthesizedTransport(60133, Vector3.Zero, 0);

        harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, OutOfRange(AzerothCoreBoat, TrinityCoreBoat),
            harness.Client.HandleUpdateObject);

        var removed = harness.ClientWire.Sent.Select(s => s.Packet).OfType<UpdateObject>()
            .SelectMany(u => u.OutOfRangeGuids).ToList();
        Assert.Contains(acBoat, removed);
        Assert.DoesNotContain(tcBoat, removed);
        Assert.False(transports.ContainsKey(acBoat));
        Assert.True(transports.ContainsKey(tcBoat));
    }
}
