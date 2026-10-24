using System;
using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// The 4.4.2 client reads SMSG_STAND_STATE_UPDATE as the state byte and then the anim kit; 3.4.3's
/// is the anim kit and then the state.
/// </summary>
public sealed class StandStateUpdateLayoutTests
{
    [Fact]
    public void Layouts_ByBuild()
    {
        Assert.IsType<StandStateUpdate.AnimKitFirstLayout>(StandStateUpdate.Layouts.For(ClientVersionBuild.V2_5_3_41750));
        Assert.IsType<StandStateUpdate.AnimKitFirstLayout>(StandStateUpdate.Layouts.For(ClientVersionBuild.V3_4_3_54261));
        Assert.IsType<StandStateUpdate.StateFirstLayout>(StandStateUpdate.Layouts.For(ClientVersionBuild.V4_4_2_60895));
    }

    [Fact]
    public void AnimKitFirstLayout_MatchesTheOldWriter()
    {
        var packet = Sitting();

        using var old = new WorldPacket(1u);
        old.WriteUInt32(packet.AnimKitID);
        old.WriteUInt8(packet.StandState);

        Assert.Equal(old.GetDataSpan().ToArray(), Write(new StandStateUpdate.AnimKitFirstLayout(), packet));
        Assert.Equal(old.GetDataSpan().ToArray(), WriteToSpan(new StandStateUpdate.AnimKitFirstLayout(), packet));
    }

    [Fact]
    public void StateFirstLayout_WritesTheStateBeforeTheAnimKit()
    {
        byte[] expected = [1, 0, 0, 0, 0];

        Assert.Equal(expected, Write(new StandStateUpdate.StateFirstLayout(), Sitting()));
        Assert.Equal(expected, WriteToSpan(new StandStateUpdate.StateFirstLayout(), Sitting()));
    }

    private static StandStateUpdate Sitting() => new() { StandState = 1, AnimKitID = 0 };

    private static byte[] Write(ServerPacketLayout<StandStateUpdate> layout, StandStateUpdate packet)
    {
        using var data = new WorldPacket(1u);
        layout.Write(packet, data);
        return data.GetDataSpan().ToArray();
    }

    private static byte[] WriteToSpan(ServerPacketLayout<StandStateUpdate> layout, StandStateUpdate packet)
    {
        Span<byte> buffer = stackalloc byte[layout.MaxSize];
        return buffer[..layout.WriteToSpan(packet, buffer)].ToArray();
    }
}
