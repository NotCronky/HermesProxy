using HermesProxy.Enums;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// SMSG_SUPERCEDED_SPELL names a new spell and the one it replaces. 3.3.5a writes the old one first
/// (TrinityCore 3.3.5 <c>Player::SendSupercededSpell</c>), 4.3.4 the new one first (TrinityCore 4.3.4
/// <c>SupercededSpells::Write</c>). Read the 3.3.5a way, a 4.3.4 Journeyman Riding over Apprentice
/// Riding reached the client as Apprentice replacing Journeyman.
/// </summary>
/// <remarks>Each fact runs only under its own legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class SupercededSpellsTests
{
    private const uint ApprenticeRiding = 33388;
    private const uint JourneymanRiding = 33391;

    private static SupercededSpells Deliver(uint first, uint second)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_SUPERCEDED_SPELLS, p =>
        {
            p.WriteUInt32(first);
            p.WriteUInt32(second);
        });

        harness.Deliver(Opcode.SMSG_SUPERCEDED_SPELLS, wire, harness.Client.HandleSupercededSpells);

        var sent = Assert.Single(harness.ClientWire.Sent);
        return Assert.IsType<SupercededSpells>(sent.Packet);
    }

    [Fact]
    public void Cata_NewSpellFirst_ReplacesTheOldOne()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var spells = Deliver(JourneymanRiding, ApprenticeRiding);

        Assert.Equal([JourneymanRiding], spells.SpellID);
        Assert.Equal([ApprenticeRiding], spells.Superceded);
    }

    [Fact]
    public void Wotlk_OldSpellFirst_ReplacesTheOldOne()
    {
        if (WorldClient.IsCataLegacy || LegacyVersion.Build != ClientVersionBuild.V3_3_5a_12340)
            return;

        var spells = Deliver(ApprenticeRiding, JourneymanRiding);

        Assert.Equal([JourneymanRiding], spells.SpellID);
        Assert.Equal([ApprenticeRiding], spells.Superceded);
    }
}
