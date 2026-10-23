using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A 3.3.5a server tells of runes a spell made ready at once (Blood Tap, Empower Rune Weapon) only
/// in the cast's SMSG_SPELL_GO rune list: the runes ready before the cast, the runes ready after it,
/// and no bytes for runes it made ready. TrinityCore 3.4.3 sends SMSG_ADD_RUNE_POWER with them, and
/// the 3.4.3 client goes by that: without it, Blood Tap's rune went on recharging in the client.
/// The mask before is no good for which runes: a triggered cast has none ready before it, and
/// AzerothCore takes it again at each rune effect, so Empower Rune Weapon's shows only its last.
/// From AzerothCore, a level 80 death knight (player 703).
/// </summary>
public sealed class DkAddRunePowerTests
{
    // a received packet starts with its opcode, which the handlers never look at
    private const string SpellGoOpcode = "3201";

    // Blood Tap after Blood Strike: rune 1 was recharging (0x3E), the cast made it ready (0x3F).
    private const string BloodTap =
        "03BF0203BF0200D9B100000001240051393C0B01BF02000000000000000200000000" + "3E3F";

    // The same cast using a rune instead: rune 5 (0x3F before, 0x2F after) and its cooldown byte.
    private const string RuneUsed =
        "03BF0203BF0200D9B100000001240051393C0B01BF02000000000000000200000000" + "3F2F00";

    // Empower Rune Weapon, without its masks.
    private const string EmpowerRuneWeapon =
        "03BF0203BF0200D0B900000009240030FA530B01BF0200000000000000020000000000000000";

    // As the server sent them: Icy Touch, Blood Strike and its Desolation (triggered), Plague
    // Strike and a triggered Desecration, then Empower Rune Weapon. AzerothCore sends its Blood Tap
    // visual reset (47804, triggered) first, with the runes the first rune effect made ready.
    private static readonly string[] Rotation =
    [
        "03BF0203BF0200F5C200000009240010E6530B011506021D7F0030F10002000000DF1506021D7F30F1000000003F2F00",
        "03BF0203BF02000AC3000000092400A7EC530B011506021D7F0030F10002000000DF1506021D7F30F1000000002F2E00",
        "03BF0203BF0200F304010001092400A7EC530B01BF0200000000000000020000000000000000002E",
        "03BF0203BF020001C300000009240051F3530B011506021D7F0030F10002000000DF1506021D7F30F1000000002E2A00",
        "03BF0203BF02009E0C01000109240051F3530B00004000000000000E0AC666661B4494C2BA4200000000002A00",
        "03BF0203BF0200BCBA00000109240030FA530B01BF0200000000000000020000000000000000003B",
        EmpowerRuneWeapon + "3B3F",
    ];

    private static LegacyHandlerHarness InWorld()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 703));
        harness.Session.GameState.RuneState = new RuneStateData();
        return harness;
    }

    private static void Deliver(LegacyHandlerHarness harness, string spellGo) =>
        harness.Deliver(Opcode.SMSG_SPELL_GO, Convert.FromHexString(SpellGoOpcode + spellGo), harness.Client.HandleSpellGo);

    [Fact]
    public void BloodTap_TellsTheClientOfTheRuneItMadeReady()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater && LegacyVersion.ExpansionVersion < 4,
            "a WotLK Classic client on a 3.3.5a server");
        var harness = InWorld();
        harness.Session.GameState.RuneState!.Cooldowns[0] = 0;

        Deliver(harness, BloodTap);

        var sent = harness.ClientWire.Sent;
        int go = sent.FindIndex(s => s.Packet is SpellGo);
        int added = sent.FindIndex(s => s.Packet is AddRunePower);
        Assert.True(go >= 0 && added > go, "SMSG_ADD_RUNE_POWER after the SMSG_SPELL_GO");
        Assert.Equal(Opcode.SMSG_ADD_RUNE_POWER, sent[added].Opcode);
        Assert.Equal([0x01, 0, 0, 0], sent[added].Bytes);
    }

    [Fact]
    public void RuneCast_MakesNoRuneReady()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater && LegacyVersion.ExpansionVersion < 4,
            "a WotLK Classic client on a 3.3.5a server");
        var harness = InWorld();

        Deliver(harness, RuneUsed);

        Assert.Contains(harness.ClientWire.Sent, s => s.Packet is SpellGo);
        Assert.DoesNotContain(harness.ClientWire.Sent, s => s.Packet is AddRunePower);
    }

    [Fact]
    public void EmpowerRuneWeapon_TellsOfEveryRuneItMadeReady_AndTriggeredCastsOfNone()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater && LegacyVersion.ExpansionVersion < 4,
            "a WotLK Classic client on a 3.3.5a server");
        var harness = InWorld();

        foreach (var spellGo in Rotation)
            Deliver(harness, spellGo);

        // runes 1 and 5 from the visual reset, rune 3 from the cast: the three the rotation used
        var added = harness.ClientWire.Sent.Where(s => s.Packet is AddRunePower).Select(s => s.Bytes).ToList();
        Assert.Equal(2, added.Count);
        Assert.Equal([0x11, 0, 0, 0], added[0]);
        Assert.Equal([0x04, 0, 0, 0], added[1]);
    }

    [Fact]
    public void EmpowerRuneWeapon_WithEveryRuneOnCooldown_TellsOfThemAll()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater && LegacyVersion.ExpansionVersion < 4,
            "a WotLK Classic client on a 3.3.5a server");
        var harness = InWorld();
        Array.Fill(harness.Session.GameState.RuneState!.Cooldowns, (byte)0);

        Deliver(harness, EmpowerRuneWeapon + "003F");

        var sent = Assert.Single(harness.ClientWire.Sent, s => s.Packet is AddRunePower);
        Assert.Equal([0x3F, 0, 0, 0], sent.Bytes);
    }

    [Fact]
    public void AnotherPlayersBloodTap_SendsNothingOfOurRunes()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater && LegacyVersion.ExpansionVersion < 4,
            "a WotLK Classic client on a 3.3.5a server");
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 5));
        harness.AddKnownObject(new WowGuid64(HighGuidTypeLegacy.Player, 703), ObjectType.Player);
        harness.Session.GameState.RuneState = new RuneStateData();
        harness.Session.GameState.RuneState.Cooldowns[0] = 0;

        Deliver(harness, BloodTap);

        Assert.DoesNotContain(harness.ClientWire.Sent, s => s.Packet is AddRunePower);
    }
}
