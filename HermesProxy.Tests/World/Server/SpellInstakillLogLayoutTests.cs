using System;
using System.Buffers;
using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// <see cref="SpellInstakillLog"/>: the older clients keep the target first, the bytes the single
/// serialiser gave before the layouts; from 4.4.2 the caster comes first, the GUID the client logs
/// as the killer.
/// </summary>
public sealed class SpellInstakillLogLayoutTests
{
    private static readonly WowGuid128 Caster = new(4, 0x0800040000000000);
    private static readonly WowGuid128 Target = new(0x00A8E00000012B00, 0x2C30040000000000);

    [Fact]
    public void Layouts_ByBuild()
    {
        Assert.False(CasterFirst(SpellInstakillLog.Layouts.For(ClientVersionBuild.V1_14_2_42597)));
        Assert.False(CasterFirst(SpellInstakillLog.Layouts.For(ClientVersionBuild.V2_5_3_41750)));
        Assert.False(CasterFirst(SpellInstakillLog.Layouts.For(ClientVersionBuild.V3_4_3_54261)));
        Assert.True(CasterFirst(SpellInstakillLog.Layouts.For(ClientVersionBuild.V4_4_2_60895)));
    }

    [Fact]
    public void TargetFirst_MatchesTheOldSerialiser()
        => AssertBothSerialisers(new SpellInstakillLog.GuidOrderLayout(casterFirst: false), Expected(Target, Caster));

    [Fact]
    public void CasterFirst_PutsTheKillerFirst()
        => AssertBothSerialisers(new SpellInstakillLog.GuidOrderLayout(casterFirst: true), Expected(Caster, Target));

    private static bool CasterFirst(ServerPacketLayout<SpellInstakillLog> layout)
    {
        using var data = new WorldPacket(1u);
        layout.Write(Packet(), data);
        return data.GetDataSpan().SequenceEqual(Expected(Caster, Target));
    }

    private static SpellInstakillLog Packet() => new() { CasterGUID = Caster, TargetGUID = Target, SpellID = 56061 };

    private static byte[] Expected(WowGuid128 first, WowGuid128 second)
    {
        using var w = new WorldPacket(1u);
        w.WritePackedGuid128(first);
        w.WritePackedGuid128(second);
        w.WriteUInt32(56061);
        return w.GetDataSpan().ToArray();
    }

    private static void AssertBothSerialisers(ServerPacketLayout<SpellInstakillLog> layout, byte[] expected)
    {
        var packet = Packet();
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
