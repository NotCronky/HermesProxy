using System.Collections.Frozen;
using System.IO;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// Inspecting a player on a 4.3.4 server. TrinityCore 4.3.4 lists each equipped item after the
/// talents (Player::BuildEnchantmentsInfoData), and that list is what the 4.4.2 inspect window
/// gets: the visible items name a transmogrification's look and carry no gems.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class CataInspectGearTests
{
    private const uint Helm = 9_410_000;
    private const uint Cloak = 9_410_001;
    private const uint TransmogLook = 9_410_002;

    private static InspectResult Inspect()
    {
        if (!GameData.Gems.ContainsKey(3995))
            GameData.Gems = GameData.ReadGems(Path.Combine("CSV", "Gems4.csv")).ToFrozenDictionary();
        GameData.StoreItemTemplate(Helm, new ItemTemplate { InventoryType = (int)InventoryType.Head, ItemLevel = 359 });
        GameData.StoreItemTemplate(Cloak, new ItemTemplate { InventoryType = (int)InventoryType.Cloak, ItemLevel = 353 });

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        var inspected = new WowGuid64(HighGuidTypeLegacy.Player, 0x2225);
        var guid = harness.AddKnownObject(inspected, ObjectType.Player);
        harness.Session.GameState.CachedPlayers[guid] = new PlayerCache { Name = "Elpriest", ClassId = Class.Priest };
        // the helm's transmogrification: what the visible item names
        lock (harness.Session.GameState.ObjectCacheLock)
            harness.Session.GameState.ObjectCacheLegacy[guid][LegacyVersion.GetUpdateField(PlayerField.PLAYER_VISIBLE_ITEM_1_ENTRYID)] =
                new UpdateField(TransmogLook);

        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_INSPECT_TALENT, packet =>
        {
            packet.WriteUInt64(inspected.Low);
            packet.WriteUInt32(41);                 // unspent talent points
            packet.WriteUInt8(1);                   // talent groups
            packet.WriteUInt8(0);                   // the active one
            packet.WriteUInt32(0);                  // its primary tree
            packet.WriteUInt8(0);                   // talents
            packet.WriteUInt8(0);                   // glyphs

            packet.WriteUInt32((1u << 0) | (1u << 14));
            // head: a permanent enchantment and a gem in the second socket, a random suffix
            packet.WriteUInt32(Helm);
            packet.WriteUInt16((1 << 0) | (1 << 3));
            packet.WriteUInt16(4209);
            packet.WriteUInt16(3995);               // Brilliant Inferno Ruby
            packet.WriteInt16(-129);
            packet.WriteUInt8(0);                   // no maker
            packet.WriteUInt32(362);
            // back: a reforge and the suffix's four enchantments
            packet.WriteUInt32(Cloak);
            packet.WriteUInt16((1 << 8) | (1 << 10) | (1 << 11) | (1 << 12) | (1 << 13));
            packet.WriteUInt16(119);                // reforge
            packet.WriteUInt16(2803);
            packet.WriteUInt16(2804);
            packet.WriteUInt16(3727);
            packet.WriteUInt16(2822);
            packet.WriteInt16(0);
            packet.WriteUInt8(0);
            packet.WriteUInt32(0);
        });
        harness.Deliver(Opcode.SMSG_INSPECT_TALENT, wire, harness.Client.HandleInspectResult);
        return Assert.Single(harness.ClientWire.Sent.Select(s => s.Packet).OfType<InspectResult>());
    }

    [Fact]
    public void TheItemsAreTheOnesWorn_NotTheirLook()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var items = Inspect().DisplayInfo.Items;

        Assert.Equal([(byte)0, (byte)14], items.Select(i => i.Index));
        Assert.Equal([Helm, Cloak], items.Select(i => i.Item.ItemID));
    }

    [Fact]
    public void AGem_IsTheGemItemInItsSocket()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var helm = Inspect().DisplayInfo.Items[0];

        var gem = Assert.Single(helm.Gems);
        Assert.Equal(1, gem.Slot);
        Assert.Equal(52207u, gem.Item.ItemID);
    }

    [Fact]
    public void TheEnchantments_AreNumberedAsThe442ClientNumbersThem()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var items = Inspect().DisplayInfo.Items;

        Assert.Equal([(4209u, (byte)0), (3995u, (byte)3)], items[0].Enchants.Select(e => (e.Id, e.Index)));
        // the reforge is no enchantment on the 4.4.2 client; the suffix's start at 8 there
        Assert.Equal([(2803u, (byte)8), (2804u, (byte)9), (3727u, (byte)10), (2822u, (byte)11)],
            items[1].Enchants.Select(e => (e.Id, e.Index)));
    }

    [Fact]
    public void ARandomSuffix_KeepsItsSign()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var helm = Inspect().DisplayInfo.Items[0];

        Assert.Equal(unchecked((uint)-129), helm.Item.RandomPropertiesID);
        Assert.Equal(362u, helm.Item.RandomPropertiesSeed);
    }

    [Fact]
    public void TheItemLevel_IsTheAverageOfTheItemsWorn()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        Assert.Equal((int)((359f + 353f) / 16f), Inspect().ItemLevel);
    }
}
