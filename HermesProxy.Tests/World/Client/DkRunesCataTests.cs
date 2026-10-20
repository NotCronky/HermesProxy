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
/// A death knight's runes as TrinityCore 4.3.4 sends them, from a capture of a level 55 death knight
/// (player 5) on a training dummy. Its SMSG_SPELL_GO rune list has the mask before the cast, the
/// mask after it and a cooldown byte for every rune ((1 - cooldown) * 255: 255 ready, 0 just used),
/// where 3.3.5a has a byte only for the runes the cast used.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class DkRunesCataTests
{
    // Icy Touch: before 0x3F, after 0x2F (rune 5, a Frost rune, used).
    private const string IcyTouch =
        "166E0105010500A5B100000009240000000000FF8E43100132070000D86030F10002000000F33207D86030F100000000" +
        "3F2FFFFFFFFF00FF";

    // Icy Touch again, with rune 5 left by the server's regeneration a sliver above zero: it
    // spent it as ready but had never marked it ready, so the mask is 0x2F before and after.
    private const string IcyTouchSameMask =
        "166E0105010500A5B10000000924000000000041C54E10017E0900008BAC30F10002000000F37E098BAC30F100000000" +
        "2F2FFFFFFFFF00FF";

    // Plague Strike that missed: the runes are given back, so the cast used none.
    private const string PlagueStrikeMissed =
        "166E010501050096B1000000092400000000006D8E4D10000145090000D86030F10102000000F34509D86030F100000000" +
        "2E2E2CFFFFFF56FF";

    private static LegacyHandlerHarness InWorld(uint playerLow = 5)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, playerLow));
        harness.Session.GameState.RuneState = new RuneStateData();
        return harness;
    }

    private static SpellCastData Go(LegacyHandlerHarness harness, string logged)
    {
        harness.Deliver(Opcode.SMSG_SPELL_GO, Convert.FromHexString(logged), harness.Client.HandleSpellGo);
        return Assert.IsType<SpellGo>(Assert.Single(harness.ClientWire.Sent.Where(s => s.Packet is SpellGo)).Packet).Cast;
    }

    [Fact]
    public void IcyTouch_PutsTheFrostRuneItUsedOnCooldown()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = InWorld();
        var cast = Go(harness, IcyTouch);

        Assert.Equal(0x2F, cast.RemainingRunes.Count);
        Assert.Equal([255, 255, 255, 255, 0, 255], cast.RemainingRunes.Cooldowns);
        Assert.Contains(cast.RemainingPower, p => p.Type == PowerType.RuneFrost);
        Assert.Equal([255, 255, 255, 255, 0, 255], harness.Session.GameState.RuneState!.Cooldowns);
    }

    [Fact]
    public void IcyTouch_WithTheSameMaskBeforeAndAfter_StillUsesTheRune()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var cast = Go(InWorld(), IcyTouchSameMask);

        Assert.Equal(0x2F, cast.RemainingRunes.Count);
        Assert.Equal([255, 255, 255, 255, 0, 255], cast.RemainingRunes.Cooldowns);
        Assert.Contains(cast.RemainingPower, p => p.Type == PowerType.RuneFrost);
    }

    [Fact]
    public void MissedStrike_UsesNoRune()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var cast = Go(InWorld(), PlagueStrikeMissed);

        Assert.Equal(0x2E, cast.RemainingRunes.Count);
        Assert.Equal([0x2C, 255, 255, 255, 0x56, 255], cast.RemainingRunes.Cooldowns);
        Assert.DoesNotContain(cast.RemainingPower, p => p.Type is PowerType.RuneBlood or PowerType.RuneFrost or PowerType.RuneUnholy);
    }

    [Fact]
    public void AnotherDeathKnightsCast_LeavesThePlayersRunesAlone()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = InWorld(playerLow: 6);
        var cast = Go(harness, IcyTouch);

        Assert.Null(cast.RemainingRunes);
        Assert.All(harness.Session.GameState.RuneState!.Cooldowns, c => Assert.Equal(255, c));
    }
}
