using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;
using ObjectUpdateBuilder = HermesProxy.World.Objects.Version.V4_4_2_60895.ObjectUpdateBuilder;

namespace HermesProxy.Tests.World.Objects;

/// <summary>
/// 4.4.2's UnitData.PowerRegenFlatModifier (bits 118-127) and PowerRegenInterruptedFlatModifier
/// (128-137), in the power group at bit 117: a death knight's rune recharge rate beyond the 0.1 a
/// second of the PowerType table, at the power slots of the rune kinds (Blood 2, Frost 3, Unholy
/// 4). TrinityCore cata_classic writes the group per slot: both modifiers, then the power, its
/// maximum and ModPowerRegen.
/// </summary>
public sealed class RuneRegenWriterTests
{
    private static readonly WowGuid128 Player = new(3, 0x0800040000000000);

    [Fact]
    public void Update_UnholyPresence_BothModifiersOfTheThreeRuneSlots()
    {
        // 10% regeneration haste: 0.1 / (1 / 1.1) a second, sent without the table's 0.1
        float modifier = 0.1f / (1f / 1.1f) - 0.1f;
        var (builder, src) = Make();
        for (int slot = 2; slot <= 4; slot++)
        {
            src.EnsurePowerRegenFlatModifier()[slot] = modifier;
            src.EnsurePowerRegenInterruptedFlatModifier()[slot] = modifier;
        }
        using var data = new WorldPacket(1u);

        builder.WriteUpdateUnitData(data);

        string m = Hex(modifier);
        Assert.Equal(
            "18"            // blocks 3 and 4
            + "07200001"    // block 3: bits 96, 117 (the group), 120-122 (flat, slots 2-4)
            + "0000001C"    // block 4: bits 130-132 (interrupted, slots 2-4)
            + m + m         // slot 2
            + m + m         // slot 3
            + m + m,        // slot 4
            Convert.ToHexString(Bytes(data)));
    }

    [Fact]
    public void Update_WithRunicPower_InterleavedBySlot()
    {
        // The runic power is slot 0, so it goes before the modifier of slot 2.
        var (builder, src) = Make();
        src.EnsurePower()[0] = 300;
        src.EnsurePowerRegenFlatModifier()[2] = 0.25f;
        using var data = new WorldPacket(1u);

        builder.WriteUpdateUnitData(data);

        Assert.Equal(
            "18"            // blocks 3 and 4
            + "01200001"    // block 3: bits 96, 117 (the group), 120 (flat, slot 2)
            + "00000400"    // block 4: bit 138 (power, slot 0)
            + "2C010000"    // 300 runic power
            + "0000803E",   // 0.25
            Convert.ToHexString(Bytes(data)));
    }

    private static (ObjectUpdateBuilder Builder, UnitData Src) Make()
    {
        var session = (GameSessionData)RuntimeHelpers.GetUninitializedObject(typeof(GameSessionData));
        typeof(GameSessionData).GetField(nameof(GameSessionData.OriginalObjectTypes))!
            .SetValue(session, new Dictionary<WowGuid128, ObjectType>());
        session.CurrentPlayerGuid = Player;

        var globalSession = (GlobalSessionData)RuntimeHelpers.GetUninitializedObject(typeof(GlobalSessionData));
        var update = new ObjectUpdate(Player, UpdateTypeModern.Values, globalSession);
        return (new ObjectUpdateBuilder(update, session), update.UnitData);
    }

    private static string Hex(float value) => Convert.ToHexString(BitConverter.GetBytes(value));

    private static byte[] Bytes(WorldPacket data)
    {
        data.FlushBits();
        return data.GetDataSpan().ToArray();
    }
}
