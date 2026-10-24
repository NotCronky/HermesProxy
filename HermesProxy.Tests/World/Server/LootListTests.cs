using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// SMSG_LOOT_LIST, sent to a group when a creature dies: the creature, its master looter and its
/// round robin winner (#424). A 3.3.5a or 4.3.4 server writes the creature's full GUID, then the
/// two as packed GUIDs. A 3.4.3 or 4.4.2 client reads the creature, the loot object, a bit for each
/// of the two and the ones present (the client's own reader, 3.4.3 rva 6c4420: bit 7 the master
/// looter, bit 6 the round robin winner).
/// </summary>
/// <remarks>
/// The client side runs only under a 3.4.3 or 4.4.2 build pair
/// (<c>HERMES_TEST_MODERN_BUILD</c>); under the default pair the packet is not passed on.
/// </remarks>
public class LootListTests
{
    private static readonly WowGuid64 Creature = new(HighGuidTypeLegacy.Creature, 6145, 4242);
    private static readonly WowGuid64 Leader = new(HighGuidTypeLegacy.Player, 7);

    private static (LegacyHandlerHarness Harness, SentClientPacket[] Sent) Deliver(WowGuid64 master, WowGuid64 roundRobinWinner)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_LOOT_LIST, p =>
        {
            p.WriteGuid(Creature);
            p.WritePackedGuid(master);
            p.WritePackedGuid(roundRobinWinner);
        });
        harness.Deliver(Opcode.SMSG_LOOT_LIST, wire, harness.Client.HandleLootList);
        return (harness, harness.ClientWire.Sent.Where(s => s.Packet is LootList).ToArray());
    }

    [Fact]
    public void MasterLooter_IsNamedAfterTheCreatureAndItsLootObject()
    {
        var (harness, sent) = Deliver(Leader, default);
        if (!ModernVersion.IsWotLKClassicOrLater)
        {
            Assert.Empty(sent);
            return;
        }

        var list = (LootList)Assert.Single(sent).Packet;
        var gameState = harness.Session.GameState;
        Assert.Equal(Creature.To128(gameState), list.Owner);
        Assert.Equal(Creature.ToLootGuid(), list.LootObj);
        Assert.Equal(Leader.To128(gameState), list.Master);
        Assert.Equal(default, list.RoundRobinWinner);

        using var expected = new WorldPacket();
        expected.WritePackedGuid128(list.Owner);
        expected.WritePackedGuid128(list.LootObj);
        expected.WriteUInt8(0x80);
        expected.WritePackedGuid128(list.Master);
        Assert.Equal(expected.GetDataSpan().ToArray(), sent[0].Bytes);
    }

    [Fact]
    public void RoundRobinWinner_TakesTheSecondBit()
    {
        var (harness, sent) = Deliver(default, Leader);
        if (!ModernVersion.IsWotLKClassicOrLater)
        {
            Assert.Empty(sent);
            return;
        }

        var list = (LootList)Assert.Single(sent).Packet;
        Assert.Equal(default, list.Master);
        Assert.Equal(Leader.To128(harness.Session.GameState), list.RoundRobinWinner);

        using var expected = new WorldPacket();
        expected.WritePackedGuid128(list.Owner);
        expected.WritePackedGuid128(list.LootObj);
        expected.WriteUInt8(0x40);
        expected.WritePackedGuid128(list.RoundRobinWinner);
        Assert.Equal(expected.GetDataSpan().ToArray(), sent[0].Bytes);
    }

    [Fact]
    public void NoLooter_SendsNeitherBit()
    {
        var (_, sent) = Deliver(default, default);
        if (!ModernVersion.IsWotLKClassicOrLater)
        {
            Assert.Empty(sent);
            return;
        }

        byte[] bytes = Assert.Single(sent).Bytes;
        Assert.Equal(0, bytes[^1]);
    }
}
