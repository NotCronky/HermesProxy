using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// SMSG_SPELL_DAMAGE_SHIELD from a 4.3.4 server: the damage a shield did to whoever struck its
/// owner. TrinityCore 4.3.4 (SpellDamageShield::Write) ends it with what an absorb took off the
/// damage, which 3.3.5 servers don't send.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class SpellDamageShieldCataTests
{
    private const ulong Player = 0x2062;
    private const ulong Creature = 0xF13047A8000000AA;

    private static SpellDamageShield Deliver(int damage, int overkill, int school, int absorbed)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(Player));
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_SPELL_DAMAGE_SHIELD, p =>
        {
            p.WriteUInt64(Player);      // struck by the shield
            p.WriteUInt64(Creature);    // the shield's owner
            p.WriteInt32(7294);         // Retribution Aura
            p.WriteInt32(damage);
            p.WriteInt32(overkill);
            p.WriteInt32(school);
            p.WriteInt32(absorbed);
        });

        harness.Deliver(Opcode.SMSG_SPELL_DAMAGE_SHIELD, wire, harness.Client.HandleSpellDamageShield);

        return Assert.IsType<SpellDamageShield>(Assert.Single(harness.ClientWire.Sent).Packet);
    }

    [Fact]
    public void HandleSpellDamageShield_KeepsWhatWasAbsorbed()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var shield = Deliver(damage: 46, overkill: 0, school: 2, absorbed: 25);

        Assert.Equal(46, shield.Damage);
        Assert.Equal(2u, shield.SchoolMask);
        Assert.Equal(25u, shield.LogAbsorbed);
        Assert.Equal(7294u, shield.SpellID);
    }

    [Fact]
    public void HandleSpellDamageShield_NothingAbsorbed_IsZero()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        // as captured: Retribution Aura on a creature, 71 Holy to a player who struck it
        var shield = Deliver(damage: 71, overkill: 0, school: 2, absorbed: 0);

        Assert.Equal(71, shield.Damage);
        Assert.Equal(0u, shield.LogAbsorbed);
    }
}
