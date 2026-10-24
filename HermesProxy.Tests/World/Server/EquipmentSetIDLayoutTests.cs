using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// 4.4.1 moved SMSG_EQUIPMENT_SET_ID's GUID behind the type and the set's number (the 4.4.2
/// client's reader: u32, u32, u64; 3.4.3's is u64, u32, u32).
/// </summary>
public sealed class EquipmentSetIDLayoutTests
{
    [Fact]
    public void Layouts_ByBuild()
    {
        Assert.IsType<EquipmentSetID.GuidFirstLayout>(EquipmentSetID.Layouts.For(ClientVersionBuild.V2_5_3_41750));
        Assert.IsType<EquipmentSetID.GuidFirstLayout>(EquipmentSetID.Layouts.For(ClientVersionBuild.V3_4_3_54261));
        Assert.IsType<EquipmentSetID.GuidLastLayout>(EquipmentSetID.Layouts.For(ClientVersionBuild.V4_4_2_60895));
    }

    [Fact]
    public void GuidFirstLayout_MatchesTheOldWriter()
    {
        var packet = SecondSet();

        using var old = new WorldPacket(1u);
        old.WriteUInt64(packet.GUID);
        old.WriteInt32(packet.Type);
        old.WriteUInt32(packet.SetID);

        Assert.Equal(old.GetDataSpan().ToArray(), Write(new EquipmentSetID.GuidFirstLayout(), packet));
    }

    [Fact]
    public void GuidLastLayout_WritesTheGuidAfterTheSetsNumber()
    {
        var packet = SecondSet();

        Assert.Equal(
            [0, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0],
            Write(new EquipmentSetID.GuidLastLayout(), packet));
    }

    private static EquipmentSetID SecondSet() => new() { GUID = 5, Type = 0, SetID = 1 };

    private static byte[] Write(ServerPacketLayout<EquipmentSetID> layout, EquipmentSetID packet)
    {
        using var data = new WorldPacket(1u);
        layout.Write(packet, data);
        return data.GetDataSpan().ToArray();
    }
}
