using System;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// SMSG_FLIGHT_SPLINE_SYNC from a 4.3.4 core: payloads as captured in the Scarlet Enclave on
/// 2026-10-21 (<c>legacy_15595_20261021_023734_1.pkt</c>, creature 29047 on its looping path). TrinityCore
/// 4.3.4 writes the fraction of the lap, then the packed GUID; the 4.4.2 client reads the GUID first.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public class FlightSplineSyncCataTests
{
    [Theory]
    [InlineData("DD-E2-39-3F-F3-2F-01-77-71-30-F1")]
    [InlineData("24-8C-E6-3E-F3-2F-01-77-71-30-F1")]
    public void CapturedSync_NamesTheCreatureAndKeepsTheDistance(string payload)
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        byte[] body = Convert.FromHexString(payload.Replace("-", ""));
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_FLIGHT_SPLINE_SYNC, p => p.WriteBytes(body));

        harness.Deliver(Opcode.SMSG_FLIGHT_SPLINE_SYNC, wire, harness.Client.HandleFlightSplineSync);

        var sent = Assert.Single(harness.ClientWire.Sent);
        var sync = Assert.IsType<FlightSplineSync>(sent.Packet);
        var creature = new WowGuid64(HighGuidTypeLegacy.Creature, 29047, 0x12F);
        Assert.Equal(creature.To128(harness.Session.GameState), sync.Guid);
        Assert.Equal(BitConverter.ToSingle(body, 0), sync.SplineDist);

        using var expected = new WorldPacket();
        expected.WritePackedGuid128(sync.Guid);
        expected.WriteFloat(sync.SplineDist);
        Assert.Equal(expected.GetDataSpan().ToArray(), sent.Bytes);
    }
}
