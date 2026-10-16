using System;
using System.Linq;
using System.Numerics;
using Framework.IO;
using HermesProxy.World;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Dispatch;

/// <summary>
/// CMSG_MOVE_CHANGE_VEHICLE_SEATS, a seat change asked for by the player driving a vehicle (#354):
/// the client's layout (native 3.4.3 MoveChangeVehicleSeats::Read), and what goes to a 3.3.5a server
/// as CMSG_CHANGE_SEATS_ON_CONTROLLED_VEHICLE (AzerothCore HandleChangeSeatsOnControlledVehicle).
/// </summary>
public class VehicleSeatsCodecTests
{
    private static readonly WowGuid128 Vehicle = new(0x6DBE, 0x2030000000000000);

    private static readonly MovementInfo Info = new() { Position = new Vector3(5600.5f, 2010.25f, 798.0f), Orientation = 1.25f, MoveTime = 4242 };

    private static byte[] Framed(Action<WorldPacket> write)
    {
        using var w = new WorldPacket(1u);
        write(w);
        byte[] payload = w.GetData();
        byte[] framed = new byte[payload.Length + 2];
        payload.CopyTo(framed, 2);
        return framed;
    }

    private static SpanPacketReader ReaderOver(byte[] framed)
        => new(new WorldPacket(framed).GetRemainingSpan());

    [Fact]
    public void MoveChangeVehicleSeats_ReadsTheMovementAndTheSeat()
    {
        byte[] f = Framed(w =>
        {
            ModernMovementCodec.Write(w, Vehicle, in Info);
            w.WritePackedGuid128(Vehicle);
            w.WriteUInt8(2);
        });

        var r = ReaderOver(f);
        MoveChangeVehicleSeatsCodec.Read(ref r, out var packet);
        Assert.Equal(Vehicle, packet.Mover);
        Assert.Equal(Info.Position, packet.MoveInfo.Position);
        Assert.Equal(4242u, packet.MoveInfo.MoveTime);
        Assert.Equal(Vehicle, packet.DstVehicle);
        Assert.Equal(2, packet.DstSeatIndex);
        Assert.Equal(0, r.Remaining);
    }

    [Theory]
    [InlineData(0, (sbyte)1)]          // any seat but 255: the next one
    [InlineData(255, (sbyte)-1)]       // 255: the previous one
    public void WriteLegacyChangeSeats_WithoutAVehicle_TheSeatSaysWhichWay(byte dstSeat, sbyte expected)
    {
        byte[] written = Legacy(new MoveChangeVehicleSeats(Vehicle, Info, WowGuid128.Empty, dstSeat));

        Assert.Equal(expected, (sbyte)written[^1]);
        Assert.Equal(0, written[^2]);       // no accessory: an empty packed GUID
    }

    [Fact]
    public void WriteLegacyChangeSeats_TheVehicleItsMovementTheAccessoryAndTheSeat()
    {
        var accessory = new WowGuid128(0x6DC0, 0x2030000000000000);
        byte[] written = Legacy(new MoveChangeVehicleSeats(Vehicle, Info, accessory, 3));

        using var expected = new WorldPacket();
        expected.WritePackedGuid(Vehicle.To64());
        LegacyMovementCodec.Write(expected, in Info);
        expected.WritePackedGuid(accessory.To64());
        expected.WriteInt8(3);
        Assert.Equal(expected.GetDataSpan().ToArray(), written);
    }

    private static byte[] Legacy(MoveChangeVehicleSeats change)
    {
        var harness = new HermesProxy.Tests.Support.LegacyHandlerHarness(recordClientPackets: false);
        using var packet = new WorldPacket();
        MovementSystem.WriteLegacyChangeSeats(packet, change, harness.Session.GameState);
        return packet.GetDataSpan().ToArray();
    }
}
