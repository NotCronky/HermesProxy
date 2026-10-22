using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// The 4.4.2 client reads SMSG_USE_EQUIPMENT_SET_RESULT as an int32 reason and then the set's GUID;
/// 3.4.3's is the GUID and then a one-byte reason.
/// </summary>
public sealed class UseEquipmentSetResultLayoutTests
{
    [Fact]
    public void Layouts_ByBuild()
    {
        Assert.IsType<UseEquipmentSetResult.GuidFirstLayout>(UseEquipmentSetResult.Layouts.For(ClientVersionBuild.V2_5_3_41750));
        Assert.IsType<UseEquipmentSetResult.GuidFirstLayout>(UseEquipmentSetResult.Layouts.For(ClientVersionBuild.V3_4_3_54261));
        Assert.IsType<UseEquipmentSetResult.ReasonFirstLayout>(UseEquipmentSetResult.Layouts.For(ClientVersionBuild.V4_4_2_60895));
    }

    [Fact]
    public void GuidFirstLayout_MatchesTheOldWriter()
    {
        var packet = SecondSetOn();

        using var old = new WorldPacket(1u);
        old.WriteUInt64(packet.GUID);
        old.WriteUInt8(packet.Reason);

        Assert.Equal(old.GetDataSpan().ToArray(), Write(new UseEquipmentSetResult.GuidFirstLayout(), packet));
    }

    [Fact]
    public void ReasonFirstLayout_WritesAnIntReasonBeforeTheGuid()
    {
        Assert.Equal(
            [0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0],
            Write(new UseEquipmentSetResult.ReasonFirstLayout(), SecondSetOn()));
    }

    private static UseEquipmentSetResult SecondSetOn() => new() { GUID = 5, Reason = 0 };

    private static byte[] Write(ServerPacketLayout<UseEquipmentSetResult> layout, UseEquipmentSetResult packet)
    {
        using var data = new WorldPacket(1u);
        layout.Write(packet, data);
        return data.GetDataSpan().ToArray();
    }
}
