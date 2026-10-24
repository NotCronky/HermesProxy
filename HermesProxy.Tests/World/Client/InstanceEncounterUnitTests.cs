using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A boss's unit frame. Both legacy servers send SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT with a frame
/// type, then for engage, disengage and priority the boss's packed GUID and a priority byte
/// (AzerothCore and TrinityCore 4.3.4 <c>InstanceScript::SendEncounterUnit</c>); 4.3.4 numbers the
/// types two higher. A modern client has a packet for each, and without them no boss frame showed.
/// </summary>
/// <remarks>
/// The modern packets exist from 4.4.2 on (<c>HERMES_TEST_MODERN_BUILD</c>); on a client without
/// them nothing is sent.
/// </remarks>
public sealed class InstanceEncounterUnitTests
{
    private const uint GeneralUmbriss = 39625;

    private static readonly WowGuid64 Boss = new(HighGuidTypeLegacy.Creature, GeneralUmbriss, 0x2D);

    private static bool ClientHasBossFrames
        => ModernVersion.GetCurrentOpcode(Opcode.SMSG_INSTANCE_ENCOUNTER_ENGAGE_UNIT) != 0;

    private static LegacyHandlerHarness Deliver(uint type, byte priority = 0)
    {
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT, p =>
        {
            p.WriteUInt32(type);
            p.WritePackedGuid(Boss);
            p.WriteUInt8(priority);
        });

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.Deliver(Opcode.SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT, wire,
            WorldClient.IsCataLegacy ? harness.Client.HandleUpdateInstanceEncounterUnitCata : harness.Client.HandleUpdateInstanceEncounterUnit);
        return harness;
    }

    private static uint Type(EncounterFrameType wotlk, EncounterFrameTypeCata cata)
        => WorldClient.IsCataLegacy ? (uint)cata : (uint)wotlk;

    [Fact]
    public void Engage_PutsTheBossFrameUp()
    {
        var harness = Deliver(Type(EncounterFrameType.Engage, EncounterFrameTypeCata.Engage), priority: 1);
        if (!ClientHasBossFrames)
        {
            Assert.Empty(harness.ClientWire.Sent);
            return;
        }

        var engage = Assert.IsType<InstanceEncounterEngageUnit>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(Boss.To128(harness.Session.GameState), engage.Unit);
        Assert.Equal(GeneralUmbriss, engage.Unit.GetEntry());
        Assert.Equal(1, engage.TargetFramePriority);
    }

    [Fact]
    public void Disengage_TakesTheBossFrameDown()
    {
        var harness = Deliver(Type(EncounterFrameType.Disengage, EncounterFrameTypeCata.Disengage));
        if (!ClientHasBossFrames)
        {
            Assert.Empty(harness.ClientWire.Sent);
            return;
        }

        var disengage = Assert.IsType<InstanceEncounterDisengageUnit>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(Boss.To128(harness.Session.GameState), disengage.Unit);
    }

    [Fact]
    public void UpdatePriority_MovesTheBossFrame()
    {
        var harness = Deliver(Type(EncounterFrameType.UpdatePriority, EncounterFrameTypeCata.UpdatePriority), priority: 2);
        if (!ClientHasBossFrames)
        {
            Assert.Empty(harness.ClientWire.Sent);
            return;
        }

        var priority = Assert.IsType<InstanceEncounterChangePriority>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(Boss.To128(harness.Session.GameState), priority.Unit);
        Assert.Equal(2, priority.TargetFramePriority);
    }

    [Fact]
    public void CataCombatResurrectionLimit_SendsNothing()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        // TrinityCore 4.3.4 sends it when a boss is pulled, before the engage, with a lone byte.
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT, p =>
        {
            p.WriteUInt32((uint)EncounterFrameTypeCata.SetCombatResLimit);
            p.WriteUInt8(0);
        });
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.Deliver(Opcode.SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT, wire, harness.Client.HandleUpdateInstanceEncounterUnitCata);

        Assert.Empty(harness.ClientWire.Sent);
    }
}
