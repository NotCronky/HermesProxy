using System;
using System.Linq;
using HermesProxy.Enums;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// TrinityCore 4.3.4 sends a power's regeneration as what it adds to the power type's base
/// (<c>Unit::UpdatePowerRegen</c>), in the class's power slot; mana's is the whole rate. The 4.4.2
/// client adds the same bases from its PowerType table and counts the power back on its own between
/// updates. Without the modifiers it counted a hunter's focus back at the bare 5 a second while the
/// server gave 4, and let a shot go out that the server refused.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class PowerRegenCataTests
{
    private static ObjectUpdate DeliverRegen(Class classId, PowerType displayPower, float flat, float interrupted)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var legacyGuid = new WowGuid64(HighGuidTypeLegacy.Player, 4);
        harness.SetActivePlayer(legacyGuid);

        int flatField = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_POWER_REGEN_FLAT_MODIFIER);
        int interruptedField = LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_POWER_REGEN_INTERRUPTED_FLAT_MODIFIER);
        uint bytes0 = (uint)Race.Human | ((uint)classId << 8) | ((uint)displayPower << 24);
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            packet.WriteUInt16(0); // map
            packet.WriteUInt32(1);
            LegacyPacketBuilder.WriteValuesBlock(packet, legacyGuid, LegacyVersion.GetUpdateField(PlayerField.PLAYER_END),
                (LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BYTES_0), bytes0),
                (flatField, BitConverter.SingleToUInt32Bits(flat)),
                (interruptedField, BitConverter.SingleToUInt32Bits(interrupted)));
        });

        harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, wire, harness.Client.HandleUpdateObject);
        var packet = Assert.Single(harness.ClientWire.Sent.Select(s => s.Packet).OfType<UpdateObject>());
        return Assert.Single(packet.ObjectUpdates);
    }

    [Fact]
    public void HunterFocus_TheModifierGoesToTheFocusSlot()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var update = DeliverRegen(Class.Hunter, PowerType.Focus, -1f, -1f);

        sbyte slot = ClassPowerTypes.GetPowerSlotForClass(Class.Hunter, PowerType.Focus);
        Assert.Equal(-1f, update.UnitData.PowerRegenFlatModifier[slot]);
        Assert.Equal(-1f, update.UnitData.PowerRegenInterruptedFlatModifier[slot]);
    }

    [Fact]
    public void MageMana_TheWholeRateGoesToTheManaSlot()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var update = DeliverRegen(Class.Mage, PowerType.Mana, 2.68f, 0.32f);

        sbyte slot = ClassPowerTypes.GetPowerSlotForClass(Class.Mage, PowerType.Mana);
        Assert.Equal(2.68f, update.UnitData.PowerRegenFlatModifier[slot]);
        Assert.Equal(0.32f, update.UnitData.PowerRegenInterruptedFlatModifier[slot]);
    }
}
