using System;
using System.Buffers;
using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// SMSG_LEARNED_SPELLS: a flat list up to 2.5, per-spell bits from 3.4.3. Both serialisers of each
/// layout are held against the branches Write() had before the split. The span writer of the 3.4.3
/// shape used to give up (-1), so every learned spell on 3.4.3 and 4.4.2 went through the fallback
/// with a warning in the log (#407).
/// </summary>
public sealed class LearnedSpellsLayoutTests
{
    [Fact]
    public void Layouts_ByBuild()
    {
        Assert.IsType<LearnedSpells.FlatLayout>(LearnedSpells.Layouts.For(ClientVersionBuild.V1_14_2_42597));
        Assert.IsType<LearnedSpells.FlatLayout>(LearnedSpells.Layouts.For(ClientVersionBuild.V2_5_3_41750));
        Assert.IsType<LearnedSpells.PerSpellBitsLayout>(LearnedSpells.Layouts.For(ClientVersionBuild.V3_4_3_54261));
        Assert.IsType<LearnedSpells.PerSpellBitsLayout>(LearnedSpells.Layouts.For(ClientVersionBuild.V4_4_2_60895));
    }

    [Fact]
    public void FlatLayout_MatchesTheOldWriter()
    {
        var packet = TwoSpellsOneFavourite();

        using var old = new WorldPacket(1u);
        old.WriteInt32(packet.Spells.Count);
        old.WriteInt32(packet.FavoriteSpellID.Count);
        old.WriteUInt32(packet.SpecializationID);
        foreach (uint spell in packet.Spells)
            old.WriteUInt32(spell);
        foreach (int spell in packet.FavoriteSpellID)
            old.WriteInt32(spell);
        old.WriteBit(packet.SuppressMessaging);
        old.FlushBits();

        AssertBothSerialisers(new LearnedSpells.FlatLayout(), packet, old.GetDataSpan().ToArray());
    }

    [Fact]
    public void PerSpellBitsLayout_MatchesTheOldWriter()
    {
        var packet = TwoSpellsOneFavourite();

        using var old = new WorldPacket(1u);
        old.WriteInt32(packet.Spells.Count);
        old.WriteUInt32(packet.SpecializationID);
        old.WriteBit(packet.SuppressMessaging);
        old.FlushBits();
        foreach (uint spell in packet.Spells)
        {
            old.WriteInt32((int)spell);
            old.WriteBit(packet.FavoriteSpellID.Contains((int)spell));
            old.WriteBit(false);
            old.WriteBit(false);
            old.WriteBit(false);
            old.FlushBits();
        }

        AssertBothSerialisers(new LearnedSpells.PerSpellBitsLayout(), packet, old.GetDataSpan().ToArray());
    }

    [Fact]
    public void PerSpellBitsLayout_FitsTheSpanBudget()
    {
        var packet = new LearnedSpells { SuppressMessaging = true };
        for (uint i = 0; i < 8; i++)
            packet.Spells.Add(80000 + i);

        byte[] buffer = new byte[packet.MaxSize];
        int written = new LearnedSpells.PerSpellBitsLayout().WriteToSpan(packet, buffer);

        Assert.Equal(4 + 4 + 1 + 8 * 5, written);
    }

    private static LearnedSpells TwoSpellsOneFavourite()
    {
        var packet = new LearnedSpells { SpecializationID = 0, SuppressMessaging = true };
        packet.Spells.Add(78);
        packet.Spells.Add(88163);
        packet.FavoriteSpellID.Add(88163);
        return packet;
    }

    private static void AssertBothSerialisers(ServerPacketLayout<LearnedSpells> layout, LearnedSpells packet, byte[] expected)
    {
        using var data = new WorldPacket(1u);
        layout.Write(packet, data);
        Assert.Equal(expected, data.GetDataSpan().ToArray());

        byte[] buffer = ArrayPool<byte>.Shared.Rent(packet.MaxSize);
        try
        {
            int written = layout.WriteToSpan(packet, buffer);
            Assert.Equal(expected, buffer.AsSpan(0, written).ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
