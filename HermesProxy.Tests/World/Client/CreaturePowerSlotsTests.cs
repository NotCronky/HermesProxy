using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A modern client keeps a creature's own power type in slot 0 and its combo points in slot 2,
/// whatever its class; only a player's slots follow the class table. A 3.3.5a server gives a
/// vehicle the power type of its power display, so a warrior-class vehicle can run on energy:
/// Soo-holu (28115) in Will of the Titans, whose spells cost energy.
/// </summary>
public sealed class CreaturePowerSlotsTests
{
    private static readonly WowGuid64 SooHolu = new(HighGuidTypeLegacy.Vehicle, 28115, 41);

    private static int Field(UnitField field) => LegacyVersion.GetUpdateField(field);

    private static uint Bytes0(Class classId, PowerType displayPower)
        => ((uint)classId << 8) | ((uint)(byte)displayPower << 24);

    private static ObjectUpdate Deliver(LegacyHandlerHarness harness, params (int Field, uint Value)[] fields)
    {
        harness.ClientWire.Sent.Clear();
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            packet.WriteUInt32(1);
            LegacyPacketBuilder.WriteValuesBlock(packet, SooHolu, Field(UnitField.UNIT_END), fields);
        });
        harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, wire, harness.Client.HandleUpdateObject);

        var sent = harness.ClientWire.Sent.Select(s => s.Packet).OfType<UpdateObject>().Single();
        return Assert.Single(sent.ObjectUpdates);
    }

    private static LegacyHandlerHarness Harness()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 77));
        harness.AddKnownObject(SooHolu, ObjectType.Unit);
        return harness;
    }

    [Fact]
    public void WarriorClassVehicleOnEnergy_HasItsEnergyInSlotZero()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater, "creature power slots are translated so from 3.4.3 on");
        var harness = Harness();

        var update = Deliver(harness,
            (Field(UnitField.UNIT_FIELD_BYTES_0), Bytes0(Class.Warrior, PowerType.Energy)),
            (Field(UnitField.UNIT_FIELD_POWER1) + (int)PowerType.Energy, 60u),
            (Field(UnitField.UNIT_FIELD_MAXPOWER1) + (int)PowerType.Energy, 100u));

        Assert.Equal(60, update.UnitData.Power[0]);
        Assert.Equal(100, update.UnitData.MaxPower[0]);
        Assert.Equal(5, update.UnitData.MaxPower[ClassPowerTypes.CreatureComboPointsSlot]);
    }

    /// <summary>
    /// A new power type alone: the server sends the type and leaves the power's own fields out,
    /// since they did not change.
    /// </summary>
    [Fact]
    public void NewPowerType_MovesThatPowerIntoSlotZero()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater, "creature power slots are translated so from 3.4.3 on");
        var harness = Harness();
        Deliver(harness,
            (Field(UnitField.UNIT_FIELD_BYTES_0), Bytes0(Class.Warrior, PowerType.Mana)),
            (Field(UnitField.UNIT_FIELD_POWER1) + (int)PowerType.Mana, 900u),
            (Field(UnitField.UNIT_FIELD_POWER1) + (int)PowerType.Energy, 70u),
            (Field(UnitField.UNIT_FIELD_MAXPOWER1) + (int)PowerType.Mana, 1000u),
            (Field(UnitField.UNIT_FIELD_MAXPOWER1) + (int)PowerType.Energy, 100u));

        var update = Deliver(harness,
            (Field(UnitField.UNIT_FIELD_BYTES_0), Bytes0(Class.Warrior, PowerType.Energy)));

        Assert.Equal(70, update.UnitData.Power[0]);
        Assert.Equal(100, update.UnitData.MaxPower[0]);
    }
}
