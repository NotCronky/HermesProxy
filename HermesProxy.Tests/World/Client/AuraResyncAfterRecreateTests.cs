using System.Linq;
using HermesProxy.Enums;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A unit that leaves the client's view takes its auras with it. When it comes back the server
/// sends the whole aura list again, the same list as before for a permanent aura, and that list
/// has to reach the client: dropped as a repeat, the unit came back with none of its auras.
/// </summary>
public sealed class AuraResyncAfterRecreateTests
{
    private const uint DevotionAura = 465;

    private static readonly WowGuid64 Player = new(HighGuidTypeLegacy.Player, 77);
    private static readonly WowGuid64 Dummy = new(HighGuidTypeLegacy.Creature, 4952, 168653);

    // SMSG_AURA_UPDATE_ALL arrived with 3.0.2; before it a unit's auras were update fields.
    private static bool LegacyHasAuraUpdates => LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056);

    private static LegacyHandlerHarness NewHarness()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(Player);
        harness.AddKnownObject(Dummy, ObjectType.Unit);
        return harness;
    }

    // One permanent aura: effect 0, no caster, positive, no duration.
    private static byte[] AuraUpdateAll()
        => LegacyPacketBuilder.Build(Opcode.SMSG_AURA_UPDATE_ALL, packet =>
        {
            packet.WritePackedGuid(Dummy);
            packet.WriteUInt8(0);
            packet.WriteUInt32(DevotionAura);
            if (WorldClient.IsCataLegacy)
                packet.WriteUInt16(0x19);
            else
                packet.WriteUInt8(0x19);
            packet.WriteUInt8(1);
            packet.WriteUInt8(0);
        });

    private static void DeliverAuraUpdateAll(LegacyHandlerHarness harness)
        => harness.Deliver(Opcode.SMSG_AURA_UPDATE_ALL, AuraUpdateAll(), harness.Client.HandleAuraUpdate);

    private static int AuraUpdatesSent(LegacyHandlerHarness harness)
        => harness.ClientWire.Sent.Select(s => s.Packet).OfType<AuraUpdate>().Count();

    [Fact]
    public void RepeatedResync_OfAUnitStillInView_IsNotSentAgain()
    {
        if (!LegacyHasAuraUpdates) return;
        var harness = NewHarness();

        DeliverAuraUpdateAll(harness);
        DeliverAuraUpdateAll(harness);

        Assert.Equal(1, AuraUpdatesSent(harness));
    }

    [Fact]
    public void Resync_AfterTheUnitWasDestroyed_ReachesTheClient()
    {
        if (!LegacyHasAuraUpdates) return;
        var harness = NewHarness();
        DeliverAuraUpdateAll(harness);

        byte[] destroy = LegacyPacketBuilder.Build(Opcode.SMSG_DESTROY_OBJECT, p =>
        {
            p.WriteGuid(Dummy);
            p.WriteUInt8(0);
        });
        harness.Deliver(Opcode.SMSG_DESTROY_OBJECT, destroy, harness.Client.HandleDestroyObject);
        DeliverAuraUpdateAll(harness);

        Assert.Equal(2, AuraUpdatesSent(harness));
        var resync = harness.ClientWire.Sent.Select(s => s.Packet).OfType<AuraUpdate>().Last();
        Assert.True(resync.UpdateAll);
        Assert.Equal(DevotionAura, Assert.Single(resync.Auras).AuraData!.SpellID);
    }

    [Fact]
    public void Resync_AfterTheUnitWentOutOfRange_ReachesTheClient()
    {
        if (!LegacyHasAuraUpdates) return;
        var harness = NewHarness();
        DeliverAuraUpdateAll(harness);

        byte[] outOfRange = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            if (WorldClient.IsCataLegacy)
                packet.WriteUInt16(0); // map
            packet.WriteUInt32(1);
            // 4.3.4 numbers the out-of-range block 3
            packet.WriteUInt8(WorldClient.IsCataLegacy ? (byte)3 : (byte)UpdateTypeLegacy.FarObjects);
            packet.WriteInt32(1);
            packet.WritePackedGuid(Dummy);
        });
        harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, outOfRange, harness.Client.HandleUpdateObject);
        DeliverAuraUpdateAll(harness);

        Assert.Equal(2, AuraUpdatesSent(harness));
    }

    [Fact]
    public void Resync_AfterAWorldPort_ReachesTheClient()
    {
        if (!LegacyHasAuraUpdates) return;
        var harness = NewHarness();
        DeliverAuraUpdateAll(harness);

        // A far teleport and back: the client builds the map again and the unit is created anew.
        byte[] newWorld = LegacyPacketBuilder.Build(Opcode.SMSG_NEW_WORLD, packet =>
        {
            if (WorldClient.IsCataLegacy)
            {
                packet.WriteFloat(2502.9f);
                packet.WriteFloat(3.14f);
                packet.WriteFloat(420.8f);
                packet.WriteUInt32(609);
                packet.WriteFloat(-5593.6f);
                return;
            }
            packet.WriteUInt32(609);
            packet.WriteFloat(2502.9f);
            packet.WriteFloat(-5593.6f);
            packet.WriteFloat(420.8f);
            packet.WriteFloat(3.14f);
        });
        harness.Session.GameState.IsWaitingForNewWorld = true;
        harness.Deliver(Opcode.SMSG_NEW_WORLD, newWorld, harness.Client.HandleNewWorld);
        DeliverAuraUpdateAll(harness);

        Assert.Equal(2, AuraUpdatesSent(harness));
    }
}
