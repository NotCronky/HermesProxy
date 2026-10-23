using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A 3.3.5a server keeps a death knight's rune recharge rate per rune kind in PLAYER_RUNE_REGEN_1..3
/// (Blood, Unholy, Frost; 0.1 a second at the base rate), and Improved Unholy Presence makes it
/// 1 / 9 s in Unholy Presence. The 3.4.3 client adds the regeneration modifiers of the rune kind's
/// power slot to its PowerType table's 0.1. Without them a rune used in Unholy Presence showed ten
/// seconds of cooldown where the server took nine.
/// </summary>
/// <remarks>Runs only under a 3.4.3 modern build (<c>HERMES_TEST_MODERN_BUILD</c>).</remarks>
public sealed class RuneRegenWotLKTests
{
    [Fact]
    public void ImprovedUnholyPresence_EachRuneKindGoesToItsPowerSlot()
    {
        Assert.SkipUnless(ModernVersion.ExpansionVersion == 3 && LegacyVersion.ExpansionVersion == 3,
            "3.4.3 client on a 3.3.5a server");

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var legacyGuid = new WowGuid64(HighGuidTypeLegacy.Player, 703);
        harness.SetActivePlayer(legacyGuid);

        int runeRegen = LegacyVersion.GetUpdateField(PlayerField.PLAYER_RUNE_REGEN_1);
        uint bytes0 = (uint)Race.Human | ((uint)Class.Deathknight << 8) | ((uint)PowerType.RunicPower << 24);
        float unholy = 1f / 9f;
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            packet.WriteUInt32(1);
            LegacyPacketBuilder.WriteValuesBlock(packet, legacyGuid, LegacyVersion.GetUpdateField(PlayerField.PLAYER_END),
                (LegacyVersion.GetUpdateField(UnitField.UNIT_FIELD_BYTES_0), bytes0),
                (runeRegen, BitConverter.SingleToUInt32Bits(unholy)),
                (runeRegen + 1, BitConverter.SingleToUInt32Bits(unholy)),
                (runeRegen + 2, BitConverter.SingleToUInt32Bits(unholy)));
        });

        harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, wire, harness.Client.HandleUpdateObject);
        var packet = Assert.Single(harness.ClientWire.Sent.Select(s => s.Packet).OfType<UpdateObject>());
        var update = Assert.Single(packet.ObjectUpdates);

        float modifier = unholy - 0.1f;
        foreach (int slot in (int[])[1, 2, 3])
        {
            Assert.Equal(modifier, update.UnitData.PowerRegenFlatModifier[slot]);
            Assert.Equal(modifier, update.UnitData.PowerRegenInterruptedFlatModifier[slot]);
        }
        Assert.Null(update.UnitData.PowerRegenFlatModifier[0]);
    }
}
