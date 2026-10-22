using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// SMSG_ITEM_COOLDOWN carries no duration because every legacy core sends it from
/// Player::ApplyEquipCooldown alone: an on-use spell is locked for 30 seconds once its item is
/// equipped. The item's own cooldown is not what the server holds; sent as the duration, it locked
/// Thunderbrew's Boot Flask on the client for 30 minutes after equipping it.
/// </summary>
public sealed class ItemEquipCooldownTests
{
    private const uint Entry = 9_401_744;
    private const uint UseSpell = 8_215;

    [Fact]
    public void HandleItemCooldown_ItemWithItsOwnLongerCooldown_SendsThirtySeconds()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 7));

        var template = new ItemTemplate { Entry = Entry };
        template.TriggeredSpellIds[0] = (int)UseSpell;
        template.TriggeredSpellCooldowns[0] = 30 * 60 * 1000;
        GameData.StoreItemTemplate(Entry, template);
        GameData.SaveItemEffectSlot(Entry, UseSpell, 0);

        var itemGuid = new WowGuid64(HighGuidTypeLegacy.Item, 744);
        var item = harness.AddKnownObject(itemGuid, ObjectType.Item);
        lock (harness.Session.GameState.ObjectCacheLock)
            harness.Session.GameState.ObjectCacheLegacy[item][LegacyVersion.GetUpdateField(ObjectField.OBJECT_FIELD_ENTRY)] = new UpdateField(Entry);

        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_ITEM_COOLDOWN, p =>
        {
            p.WriteGuid(itemGuid);
            p.WriteUInt32(UseSpell);
        });
        harness.Deliver(Opcode.SMSG_ITEM_COOLDOWN, wire, harness.Client.HandleItemCooldown);

        var cooldown = Assert.IsType<ItemCooldown>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(item, cooldown.ItemGuid);
        Assert.Equal(UseSpell, cooldown.SpellID);
        Assert.Equal(30_000u, cooldown.Cooldown);
    }
}
