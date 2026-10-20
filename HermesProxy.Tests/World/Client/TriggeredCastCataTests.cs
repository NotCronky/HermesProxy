using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A triggered cast's SMSG_SPELL_GO from TrinityCore 4.3.4, which marks it with CAST_FLAG_PENDING
/// and leaves its CastFlagsEx 0. The 4.4.2 client starts the cooldown of every spell the player
/// casts unless CAST_FLAG_EX_IGNORE_COOLDOWN says not to, as TrinityCore cata_classic does for
/// triggered casts. From a capture of a level 55 death knight (player 5) casting Raise Dead.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class TriggeredCastCataTests
{
    // Raise Dead's guardian summon (46585), cast triggered: flags 0x40901.
    private const string RaiseDeadSummon =
        "166E0105010500F9B500000109040000000000D9C1861000004000000000D2761B4555CAAEC53201D2430000000000";

    // Raise Dead (46584) itself, as the player cast it: flags 0x900.
    private const string RaiseDead =
        "166E0105010500F8B500000009000000000000D9C1861001050000000000000000020000000000000000";

    private static SpellCastData Go(string logged)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 5));
        harness.Deliver(Opcode.SMSG_SPELL_GO, Convert.FromHexString(logged), harness.Client.HandleSpellGo);
        return Assert.IsType<SpellGo>(Assert.Single(harness.ClientWire.Sent.Where(s => s.Packet is SpellGo)).Packet).Cast;
    }

    [Fact]
    public void TriggeredSummon_StartsNoCooldownOnTheClient()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var cast = Go(RaiseDeadSummon);

        Assert.Equal(46585, cast.SpellID);
        Assert.Equal((uint)CastFlagEx.IgnoreCooldown, cast.CastFlagsEx);
    }

    [Fact]
    public void TheSpellThePlayerCast_StartsItsCooldown()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var cast = Go(RaiseDead);

        Assert.Equal(46584, cast.SpellID);
        Assert.Equal(0u, cast.CastFlagsEx);
    }
}
