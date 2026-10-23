using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A 3.3.5a server has one cast-time multiplier, UNIT_MOD_CAST_SPEED, and shortens the global
/// cooldown of spells with it. The 3.4.3 client shortens its own global cooldown with a second
/// one, UnitData.ModSpellHaste, which TrinityCore 3.4.3 moves together with the cast speed
/// (Unit::ApplyCastTimePercentMod). Left at 1, Power Infusion took a cast time from 10 s to
/// 8.3 s, but the client still counted a 1.5 s global cooldown where the server counted 1.25 s.
/// </summary>
public sealed class SpellHasteTests
{
    private static readonly WowGuid64 Player = new(HighGuidTypeLegacy.Player, 77);

    private static int Field(UnitField field) => LegacyVersion.GetUpdateField(field);

    private static ObjectUpdate Deliver(LegacyHandlerHarness harness, float castSpeed)
    {
        harness.ClientWire.Sent.Clear();
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            packet.WriteUInt32(1);
            LegacyPacketBuilder.WriteValuesBlock(packet, Player, Field(UnitField.UNIT_END),
                (Field(UnitField.UNIT_MOD_CAST_SPEED), BitConverter.SingleToUInt32Bits(castSpeed)));
        });
        harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, wire, harness.Client.HandleUpdateObject);

        var sent = harness.ClientWire.Sent.Select(s => s.Packet).OfType<UpdateObject>().Single();
        return Assert.Single(sent.ObjectUpdates);
    }

    private static LegacyHandlerHarness Harness()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(Player);
        harness.AddKnownObject(Player, ObjectType.Player);
        return harness;
    }

    [Theory]
    [InlineData(1f / 1.2f)]
    [InlineData(1f)]
    public void CastSpeed_IsTheSpellHasteToo(float castSpeed)
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater && LegacyVersion.ExpansionVersion < 4,
            "a WotLK Classic client on a server with a single cast-time multiplier");
        var harness = Harness();

        var update = Deliver(harness, castSpeed);

        Assert.Equal(castSpeed, update.UnitData.ModCastSpeed);
        Assert.Equal(castSpeed, update.UnitData.ModCastHaste);
    }
}
