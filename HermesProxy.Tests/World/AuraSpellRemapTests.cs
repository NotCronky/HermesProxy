using HermesProxy.World;
using HermesProxy.World.Enums;

using Xunit;

namespace HermesProxy.Tests.World;

/// <summary>
/// Issue #330 — the 3.3.5a Prayer of Mending rank 2/3 auras (48110, 48111) are
/// SPELL_AURA_MOD_VISIBILITY_RANGE in the 3.4.3 Spell DB2. Forwarded as-is, every other unit
/// disappears for the player carrying the aura.
/// </summary>
public class AuraSpellRemapTests
{
    [Theory]
    [InlineData(KnownSpellIds.PrayerOfMendingAuraRank2)]
    [InlineData(KnownSpellIds.PrayerOfMendingAuraRank3)]
    public void PrayerOfMendingRankAuras_AreShownAsTheRank1Aura(uint legacySpellId)
    {
        Assert.Equal(KnownSpellIds.PrayerOfMendingAura, AuraSpellRemap.ToModern(legacySpellId));
    }

    [Theory]
    [InlineData(KnownSpellIds.PrayerOfMendingAura)]
    [InlineData(48113u)]  // Prayer of Mending (Rank 3) — the cast, not the aura
    [InlineData(41637u)]  // Prayer of Mending heal
    [InlineData(48066u)]  // Power Word: Shield
    [InlineData(0u)]
    public void OtherSpells_AreForwardedUnchanged(uint spellId)
    {
        Assert.Equal(spellId, AuraSpellRemap.ToModern(spellId));
    }

    [Fact]
    public void CancellingPrayerOfMending_CancelsEveryLegacyRank()
    {
        Assert.Equal(
            [KnownSpellIds.PrayerOfMendingAura, KnownSpellIds.PrayerOfMendingAuraRank2, KnownSpellIds.PrayerOfMendingAuraRank3],
            AuraSpellRemap.ToLegacyCancelCandidates(KnownSpellIds.PrayerOfMendingAura));
    }

    [Fact]
    public void CancellingAnyOtherAura_CancelsOnlyThatAura()
    {
        Assert.Equal([48066u], AuraSpellRemap.ToLegacyCancelCandidates(48066u));
    }
}
