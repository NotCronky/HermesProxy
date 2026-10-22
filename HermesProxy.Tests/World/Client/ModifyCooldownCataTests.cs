using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// SMSG_MODIFY_COOLDOWN from a 4.3.4 core: TrinityCore 4.3.4 SpellHistory::ModifyCooldown writes the
/// spell, the unit's GUID and a signed number of milliseconds; the 4.4.2 client takes the spell, the
/// milliseconds and whether the cooldown is the pet's.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public class ModifyCooldownCataTests
{
    [Theory]
    [InlineData(1680u, HighGuidTypeLegacy.Player, -6000, false)] // Whirlwind on four targets
    [InlineData(17253u, HighGuidTypeLegacy.Pet, 2500, true)]
    public void ModifyCooldown_KeepsTheSpellAndTheTime(uint spellId, HighGuidTypeLegacy unit, int deltaMs, bool isPet)
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        var guid = unit == HighGuidTypeLegacy.Pet ? new WowGuid64(unit, 165, 7) : new WowGuid64(unit, 1);
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_MODIFY_COOLDOWN, p =>
        {
            p.WriteUInt32(spellId);
            p.WriteUInt64(guid.Low);
            p.WriteInt32(deltaMs);
        });

        harness.Deliver(Opcode.SMSG_MODIFY_COOLDOWN, wire, harness.Client.HandleModifyCooldown);

        var sent = Assert.Single(harness.ClientWire.Sent);
        var modify = Assert.IsType<ModifyCooldown>(sent.Packet);
        Assert.Equal((int)spellId, modify.SpellID);
        Assert.Equal(deltaMs, modify.DeltaTime);
        Assert.Equal(isPet, modify.IsPet);

        using var expected = new WorldPacket();
        expected.WriteInt32((int)spellId);
        expected.WriteInt32(deltaMs);
        expected.WriteBit(isPet);
        expected.WriteBit(false);
        expected.FlushBits();
        Assert.Equal(expected.GetDataSpan().ToArray(), sent.Bytes);
    }
}
