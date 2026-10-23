using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A 3.3.5a server keeps combo points on the unit that built them and sends a vehicle's to the
/// player driving it as SMSG_PET_UPDATE_COMBO_POINTS. A modern client keeps them on the vehicle
/// too: a native server creates every creature with MaxPower[2] = 5, and while the player rides,
/// the combo frame reads GetComboPoints("vehicle", "target"). The Wyrmrest Skytalon's Flame Spike
/// builds them and Engulf in Flames spends them.
/// </summary>
public sealed class VehicleComboPointsTests
{
    private static readonly WowGuid64 Player = new(HighGuidTypeLegacy.Player, 77);
    private static readonly WowGuid64 Skytalon = new(HighGuidTypeLegacy.Vehicle, 30161, 55);
    private static readonly WowGuid64 Target = new(HighGuidTypeLegacy.Creature, 30245, 90);

    private static byte[] PetComboPoints(WowGuid64 unit, WowGuid64 target, byte points)
        => LegacyPacketBuilder.Build(Opcode.SMSG_PET_UPDATE_COMBO_POINTS, packet =>
        {
            packet.WritePackedGuid(unit);
            packet.WritePackedGuid(target);
            packet.WriteUInt8(points);
        });

    private static LegacyHandlerHarness Riding(out WowGuid128 vehicle)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(Player);
        harness.AddKnownObject(Target, ObjectType.Unit);
        vehicle = harness.AddKnownObject(Skytalon, ObjectType.Unit);
        harness.Session.GameState.SetVehicleRecId(vehicle, 165);
        return harness;
    }

    private static void Deliver(LegacyHandlerHarness harness, WowGuid64 unit, WowGuid64 target, byte points)
        => harness.Deliver(Opcode.SMSG_PET_UPDATE_COMBO_POINTS, PetComboPoints(unit, target, points),
            harness.Client.HandlePetUpdateComboPoints);

    private static ObjectUpdate SingleUpdate(LegacyHandlerHarness harness)
    {
        var sent = Assert.Single(harness.ClientWire.Sent);
        Assert.Equal(Opcode.SMSG_UPDATE_OBJECT, sent.Opcode);
        return Assert.Single(Assert.IsType<UpdateObject>(sent.Packet).ObjectUpdates);
    }

    [Fact]
    public void VehicleComboPoints_GoOnTheVehicle()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater, "a vehicle's combo points are translated from 3.4.3 on");
        var harness = Riding(out var vehicle);

        Deliver(harness, Skytalon, Target, 3);

        var update = SingleUpdate(harness);
        Assert.Equal(vehicle, update.Guid);
        Assert.Equal(Target.To128(harness.Session.GameState), update.UnitData.ComboTarget);
        Assert.Equal(3, update.UnitData.Power[ClassPowerTypes.CreatureComboPointsSlot]);
        Assert.Equal(5, update.UnitData.MaxPower[ClassPowerTypes.CreatureComboPointsSlot]);
    }

    /// <summary>A finisher or a lost target: the server clears them with no target.</summary>
    [Fact]
    public void ClearedVehicleComboPoints_EmptyTheVehicleSlot()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater, "a vehicle's combo points are translated from 3.4.3 on");
        var harness = Riding(out _);

        Deliver(harness, Skytalon, WowGuid64.Empty, 0);

        var update = SingleUpdate(harness);
        Assert.Equal(WowGuid128.Empty, update.UnitData.ComboTarget);
        Assert.Equal(0, update.UnitData.Power[ClassPowerTypes.CreatureComboPointsSlot]);
    }

    /// <summary>A pet or a bot is no vehicle; no client frame shows its combo points.</summary>
    [Fact]
    public void ComboPointsOfAUnitThatIsNoVehicle_AreDropped()
    {
        var harness = Riding(out _);

        Deliver(harness, Target, Player, 2);

        Assert.Empty(harness.ClientWire.Sent);
    }

    /// <summary>The player's own copy; its SMSG_UPDATE_COMBO_POINTS twin is the one translated.</summary>
    [Fact]
    public void ThePlayersOwnComboPoints_AreDropped()
    {
        var harness = Riding(out _);

        Deliver(harness, Player, Target, 2);

        Assert.Empty(harness.ClientWire.Sent);
    }
}
