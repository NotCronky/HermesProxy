using System;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// SMSG_SPELL_INTERRUPT_LOG from a 4.3.4 core (#407): payloads as captured in battlegrounds on
/// 2026-10-12/13 (<c>legacy_15595_20261012_231949_1.pkt</c>, <c>legacy_15595_20261013_002535_1.pkt</c>),
/// read in WowPacketParser's 4.3.4 order and passed on as the 4.4.2 packet.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public class SpellInterruptLogCataTests
{
    [Theory]
    [InlineData("14-81-61-E6-06-00-00-20-28-20-62-88-00-00", 0x2129ul, 0x2160ul, 1766, 34914)]  // Kick, Vampiric Touch
    [InlineData("14-81-64-5B-08-00-00-20-5E-21-5C-01-00-00", 0x205Ful, 0x2165ul, 2139, 348)]    // Counterspell, Immolate
    public void CapturedInterrupt_NamesBothPlayersAndBothSpells(string payload, ulong caster, ulong victim, int spellId, int interruptedSpellId)
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        byte[] body = Convert.FromHexString(payload.Replace("-", ""));
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_SPELL_INTERRUPT_LOG, p => p.WriteBytes(body));

        harness.Deliver(Opcode.SMSG_SPELL_INTERRUPT_LOG, wire, harness.Client.HandleSpellInterruptLog);

        var sent = Assert.Single(harness.ClientWire.Sent);
        var log = Assert.IsType<SpellInterruptLog>(sent.Packet);
        var gameState = harness.Session.GameState;
        Assert.Equal(new WowGuid64(caster).To128(gameState), log.Caster);
        Assert.Equal(new WowGuid64(victim).To128(gameState), log.Victim);
        Assert.Equal(spellId, log.SpellID);
        Assert.Equal(interruptedSpellId, log.InterruptedSpellID);

        using var expected = new WorldPacket();
        expected.WritePackedGuid128(log.Caster);
        expected.WritePackedGuid128(log.Victim);
        expected.WriteInt32(interruptedSpellId);
        expected.WriteInt32(spellId);
        Assert.Equal(expected.GetDataSpan().ToArray(), sent.Bytes);
    }
}
