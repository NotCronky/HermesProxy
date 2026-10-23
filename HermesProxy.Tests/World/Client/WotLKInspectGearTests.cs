using System.Collections.Frozen;
using System.IO;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// Inspecting a player on a 3.3.5a server. AzerothCore, TrinityCore and cmangos list each equipped
/// item after the talents (Player::BuildEnchantmentsInfoData), and that list is what the inspect
/// window gets: the visible items carry no enchantment, gem or random suffix.
/// </summary>
/// <remarks>Runs only under a 3.3.5a legacy build, the default.</remarks>
public sealed class WotLKInspectGearTests
{
    private const uint Helm = 9_420_000;
    private const uint Cloak = 9_420_001;
    private const uint VisibleHelm = 9_420_002;

    private static InspectResult Inspect()
    {
        if (!GameData.Gems.ContainsKey(3520))
            GameData.Gems = GameData.ReadGems(Path.Combine("CSV", "Gems3.csv")).ToFrozenDictionary();

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        var inspected = new WowGuid64(HighGuidTypeLegacy.Player, 0xFAC);
        var guid = harness.AddKnownObject(inspected, ObjectType.Player);
        harness.Session.GameState.CachedPlayers[guid] = new PlayerCache { Name = "Helianora", ClassId = Class.Priest };
        // what the visible item names: the helm alone, nothing on it
        lock (harness.Session.GameState.ObjectCacheLock)
            harness.Session.GameState.ObjectCacheLegacy[guid][LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID)] =
                new UpdateField(VisibleHelm);

        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_INSPECT_TALENT, packet =>
        {
            packet.WritePackedGuid(inspected);
            packet.WriteUInt32(0);                  // unspent talent points
            packet.WriteUInt8(1);                   // talent groups
            packet.WriteUInt8(0);                   // the active one
            packet.WriteUInt8(1);                   // talents
            packet.WriteUInt32(1904);
            packet.WriteUInt8(2);
            packet.WriteUInt8(0);                   // glyphs

            packet.WriteUInt32((1u << 0) | (1u << 14));
            // head: a permanent enchantment, a meta gem and a ruby (AzerothCore, a priest bot's helm)
            packet.WriteUInt32(Helm);
            packet.WriteUInt16((1 << 0) | (1 << 2) | (1 << 3));
            packet.WriteUInt16(3820);
            packet.WriteUInt16(3627);               // Insightful Earthsiege Diamond
            packet.WriteUInt16(3520);               // Runed Cardinal Ruby
            packet.WriteInt16(-39);
            packet.WriteUInt8(0);                   // no maker
            packet.WriteUInt32(98);
            // back: the suffix's three enchantments
            packet.WriteUInt32(Cloak);
            packet.WriteUInt16((1 << 7) | (1 << 8) | (1 << 9));
            packet.WriteUInt16(2803);
            packet.WriteUInt16(2804);
            packet.WriteUInt16(2822);
            packet.WriteInt16(0);
            packet.WriteUInt8(0);
            packet.WriteUInt32(0);
        });
        harness.Deliver(Opcode.SMSG_INSPECT_TALENT, wire, harness.Client.HandleInspectResult);
        return Assert.Single(harness.ClientWire.Sent.Select(s => s.Packet).OfType<InspectResult>());
    }

    [Fact]
    public void TheItemsAreTheOnesTheServerLists_NotTheVisibleOnes()
    {
        if (LegacyVersion.ExpansionVersion != 3)
            return;

        var items = Inspect().DisplayInfo.Items;

        Assert.Equal([(byte)0, (byte)14], items.Select(i => i.Index));
        Assert.Equal([Helm, Cloak], items.Select(i => i.Item.ItemID));
    }

    [Fact]
    public void TheGems_AreTheGemItemsInTheirSockets()
    {
        if (LegacyVersion.ExpansionVersion != 3)
            return;

        var helm = Inspect().DisplayInfo.Items[0];

        Assert.Equal([((byte)0, 41401u), ((byte)1, 40113u)], helm.Gems.Select(g => (g.Slot, g.Item.ItemID)));
    }

    [Fact]
    public void TheEnchantments_AreNumberedAsTheModernClientNumbersThem()
    {
        if (LegacyVersion.ExpansionVersion != 3)
            return;

        var items = Inspect().DisplayInfo.Items;

        Assert.Equal([(3820u, (byte)0), (3627u, (byte)2), (3520u, (byte)3)], items[0].Enchants.Select(e => (e.Id, e.Index)));
        // 3.3.5a has no engineering slot: its random property slots start at 7, the modern client's at 8
        Assert.Equal([(2803u, (byte)8), (2804u, (byte)9), (2822u, (byte)10)], items[1].Enchants.Select(e => (e.Id, e.Index)));
    }

    [Fact]
    public void ARandomSuffix_KeepsItsSign()
    {
        if (LegacyVersion.ExpansionVersion != 3)
            return;

        var helm = Inspect().DisplayInfo.Items[0];

        Assert.Equal(unchecked((uint)-39), helm.Item.RandomPropertiesID);
        Assert.Equal(98u, helm.Item.RandomPropertiesSeed);
    }
}
