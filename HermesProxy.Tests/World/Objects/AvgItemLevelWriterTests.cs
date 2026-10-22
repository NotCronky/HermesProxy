using System;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;
using ObjectUpdateBuilder = HermesProxy.World.Objects.Version.V4_4_2_60895.ObjectUpdateBuilder;

namespace HermesProxy.Tests.World.Objects;

/// <summary>
/// 4.4.2's PlayerData.AvgItemLevel as the client reads it (WowPacketParser's 4.4.2 reader, made
/// from the client; TrinityCore cata_classic writes it the same): six floats after
/// TaxiMountAnimKitID on a create; on an update bit 88 for the array, then 89 + i for each slot.
/// </summary>
public sealed class AvgItemLevelWriterTests
{
    private static readonly WowGuid128 Player = new(4, 0x0800040000000000);
    private const float Average = 6122f / 17f;

    private static ObjectUpdate Make(UpdateTypeModern type, bool withAverage, out LegacyHandlerHarness harness)
    {
        harness = new LegacyHandlerHarness(recordClientPackets: false);
        var update = new ObjectUpdate(Player, type, harness.Session);
        if (type != UpdateTypeModern.Values)
            update.CreateData.ObjectType = ObjectType.Player;
        float?[] slots = update.EnsurePlayerData().AvgItemLevel;
        if (withAverage)
        {
            slots[0] = Average;
            slots[1] = Average;
            slots[2] = Average;
        }
        return update;
    }

    [Fact]
    public void Update_TheArraysBitThenOneForEachOfTheThreeSlots()
    {
        var update = Make(UpdateTypeModern.Values, true, out var harness);
        using var data = new WorldPacket();
        new ObjectUpdateBuilder(update, harness.Session.GameState).WriteUpdatePlayerData(data);
        using var reader = new WorldPacket(1, data.GetDataSpan().ToArray());

        Assert.Equal(0b00100u, reader.ReadBits<uint>(5));      // block 2 alone: bits 64-95
        Assert.Equal(0x0F000000u, reader.ReadBits<uint>(32));  // 88, 89, 90, 91
        Assert.True(reader.ReadBit());                         // IsQuestLogChangesMaskSkipped
        Assert.Equal(Average, reader.ReadFloat());
        Assert.Equal(Average, reader.ReadFloat());
        Assert.Equal(Average, reader.ReadFloat());
        Assert.False(reader.CanRead());
    }

    [Fact]
    public void Create_TheThreeSlotsThenThePvpOnesAtZero()
    {
        var with = Make(UpdateTypeModern.CreateObject1, true, out var harnessWith);
        var without = Make(UpdateTypeModern.CreateObject1, false, out var harnessWithout);
        using var a = new WorldPacket();
        using var b = new WorldPacket();
        new ObjectUpdateBuilder(with, harnessWith.Session.GameState).WriteCreatePlayerData(a);
        new ObjectUpdateBuilder(without, harnessWithout.Session.GameState).WriteCreatePlayerData(b);
        byte[] withBytes = a.GetDataSpan().ToArray();
        byte[] withoutBytes = b.GetDataSpan().ToArray();

        byte[] three = new byte[12];
        for (int i = 0; i < 3; i++)
            BitConverter.TryWriteBytes(three.AsSpan(i * 4), Average);
        int at = withBytes.AsSpan().IndexOf(three);

        Assert.True(at > 0);
        Assert.Equal(withoutBytes.Length, withBytes.Length);
        Assert.Equal(new byte[24], withoutBytes.AsSpan(at, 24).ToArray());
        Assert.Equal(new byte[12], withBytes.AsSpan(at + 12, 12).ToArray());
        Assert.Equal(withoutBytes.AsSpan(0, at).ToArray(), withBytes.AsSpan(0, at).ToArray());
        Assert.Equal(withoutBytes.AsSpan(at + 12).ToArray(), withBytes.AsSpan(at + 12).ToArray());
    }
}
