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
/// No legacy server sends an average item level, and from 4.4.2 on the client's character sheet
/// shows the one in the player's object: it read 0 / 0. The proxy works it out from the items in
/// the player's equipment slots, the way TrinityCore 4.3.4's dungeon finder does.
/// </summary>
public sealed class CataAverageItemLevelTests
{
    private const uint FirstEntry = 9_400_000;

    // A priest's: head, neck, shoulders, chest, waist, legs, feet, wrists, hands, two rings, two
    // trinkets, cloak at 359; a 378 one-hander, a 359 held item and a 359 wand.
    private static readonly (int Slot, InventoryType Type, uint Level)[] Worn =
    [
        (0, InventoryType.Head, 359), (1, InventoryType.Neck, 359), (2, InventoryType.Shoulders, 359),
        (4, InventoryType.Robe, 359), (5, InventoryType.Waist, 359), (6, InventoryType.Legs, 359),
        (7, InventoryType.Feet, 359), (8, InventoryType.Wrists, 359), (9, InventoryType.Hands, 359),
        (10, InventoryType.Finger, 359), (11, InventoryType.Finger, 359), (12, InventoryType.Trinket, 359),
        (13, InventoryType.Trinket, 359), (14, InventoryType.Cloak, 359), (15, InventoryType.Weapon, 378),
        (16, InventoryType.Holdable, 359), (17, InventoryType.RangedRight, 359),
    ];

    private static ObjectUpdate Deliver(bool withEquipment)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var legacyGuid = new WowGuid64(HighGuidTypeLegacy.Player, 6);
        var guid = harness.SetActivePlayer(legacyGuid);
        harness.Session.GameState.CachedPlayers[guid] = new PlayerCache { ClassId = Class.Priest };

        int entryField = LegacyVersion.GetUpdateField(ObjectField.OBJECT_FIELD_ENTRY);
        int invSlotHead = LegacyVersion.GetUpdateField(PlayerField.PLAYER_FIELD_INV_SLOT_HEAD);
        var fields = new System.Collections.Generic.List<(int Field, uint Value)>
        {
            (LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BYTES_0), (uint)Race.Human | ((uint)Class.Priest << 8)),
        };
        foreach (var (slot, type, level) in Worn)
        {
            uint entry = FirstEntry + (uint)slot;
            GameData.StoreItemTemplate(entry, new ItemTemplate { InventoryType = (int)type, ItemLevel = level });
            var itemGuid = new WowGuid64(HighGuidTypeLegacy.Item, 100 + (uint)slot);
            var item = harness.AddKnownObject(itemGuid, ObjectType.Item);
            lock (harness.Session.GameState.ObjectCacheLock)
                harness.Session.GameState.ObjectCacheLegacy[item][entryField] = new UpdateField(entry);
            if (withEquipment)
            {
                fields.Add((invSlotHead + slot * 2, (uint)itemGuid.Low));
                fields.Add((invSlotHead + slot * 2 + 1, (uint)(itemGuid.Low >> 32)));
            }
        }

        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            if (WorldClient.IsCataLegacy)
                packet.WriteUInt16(0); // map
            packet.WriteUInt32(1);
            LegacyPacketBuilder.WriteValuesBlock(packet, legacyGuid, LegacyVersion.GetUpdateField(PlayerField.PLAYER_END),
                [.. fields]);
        });
        harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, wire, harness.Client.HandleUpdateObject);
        var sent = Assert.Single(harness.ClientWire.Sent.Select(s => s.Packet).OfType<UpdateObject>());
        return Assert.Single(sent.ObjectUpdates);
    }

    [Fact]
    public void TheEquipmentSlots_GiveTheAverageInTheFirstThreeSlots()
    {
        if (!ModernVersion.PlayerObjectCarriesAverageItemLevel || !WorldClient.IsCataLegacy)
            return;

        var update = Deliver(withEquipment: true);

        float expected = (378f + 15 * 359f + 359f) / 17f;
        Assert.NotNull(update.PlayerData);
        Assert.Equal(expected, update.PlayerData.AvgItemLevel[0]);
        Assert.Equal(expected, update.PlayerData.AvgItemLevel[1]);
        Assert.Equal(expected, update.PlayerData.AvgItemLevel[2]);
    }

    [Fact]
    public void AnUpdateThatChangesNoEquipmentSlot_LeavesTheAverageAlone()
    {
        if (!ModernVersion.PlayerObjectCarriesAverageItemLevel || !WorldClient.IsCataLegacy)
            return;

        var update = Deliver(withEquipment: false);

        Assert.Null(update.PlayerData?.AvgItemLevel[0]);
    }

    [Fact]
    public void AnOlderClient_GetsNoAverage()
    {
        if (ModernVersion.PlayerObjectCarriesAverageItemLevel)
            return;

        var update = Deliver(withEquipment: true);

        Assert.Null(update.PlayerData?.AvgItemLevel[0]);
    }
}
