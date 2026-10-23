using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;
using ObjectUpdateBuilder = HermesProxy.World.Objects.Version.V3_4_3_54261.ObjectUpdateBuilder;

namespace HermesProxy.Tests.World.Objects;

/// <summary>
/// 3.4.3's UnitData.PowerRegenFlatModifier (bits 117-126) and PowerRegenInterruptedFlatModifier
/// (127-136), in the power group at bit 116: a death knight's rune recharge rate beyond the 0.1 a
/// second of the PowerType table, at the power slots of the rune kinds (Blood 1, Frost 2, Unholy
/// 3). TrinityCore 3.4.3 writes the group per slot: both modifiers, then the power, its maximum
/// and ModPowerRegen, and a player's own create carries the ten modifier pairs ahead of the powers.
/// </summary>
public sealed class RuneRegenWriterWotLKTests
{
    private static readonly WowGuid128 Player = new(3, 0x0800040000000000);

    [Fact]
    public void Update_ImprovedUnholyPresence_BothModifiersOfTheThreeRuneSlots()
    {
        // 1 / 9 s, sent without the table's 0.1
        float modifier = 1f / 9f - 0.1f;
        var (builder, src) = Make();
        for (int slot = 1; slot <= 3; slot++)
        {
            src.EnsurePowerRegenFlatModifier()[slot] = modifier;
            src.EnsurePowerRegenInterruptedFlatModifier()[slot] = modifier;
        }
        using var data = new WorldPacket(1u);

        builder.WriteUpdateUnitData(data);

        string m = Hex(modifier);
        Assert.Equal(
            "18"            // blocks 3 and 4
            + "01D00001"    // block 3: bits 96, 116 (the group), 118-120 (flat, slots 1-3)
            + "00000007"    // block 4: bits 128-130 (interrupted, slots 1-3)
            + m + m         // slot 1
            + m + m         // slot 2
            + m + m,        // slot 3
            Convert.ToHexString(Bytes(data)));
    }

    [Fact]
    public void Update_WithRunicPower_InterleavedBySlot()
    {
        // The runic power is slot 0, so it goes before the modifier of slot 1.
        var (builder, src) = Make();
        src.EnsurePower()[0] = 300;
        src.EnsurePowerRegenFlatModifier()[1] = 0.25f;
        using var data = new WorldPacket(1u);

        builder.WriteUpdateUnitData(data);

        Assert.Equal(
            "18"            // blocks 3 and 4
            + "00500001"    // block 3: bits 96, 116 (the group), 118 (flat, slot 1)
            + "00000200"    // block 4: bit 137 (power, slot 0)
            + "2C010000"    // 300 runic power
            + "0000803E",   // 0.25
            Convert.ToHexString(Bytes(data)));
    }

    [Fact]
    public void Create_OwnerFloatPairs_CarryTheModifiersBySlot()
    {
        var (builder, src) = Make();
        src.EnsurePowerRegenFlatModifier()[2] = 0.25f;
        src.EnsurePowerRegenInterruptedFlatModifier()[2] = 0.5f;
        using var data = new WorldPacket(1u);

        builder.WriteCreateUnitOwnerFloatPairs(data, src);

        string zero = Hex(0f);
        string pair = zero + zero;
        Assert.Equal(
            pair + pair                     // slots 0 and 1
            + "0000803E" + "0000003F"       // slot 2: 0.25, 0.5
            + string.Concat(System.Linq.Enumerable.Repeat(pair, 7)),
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
