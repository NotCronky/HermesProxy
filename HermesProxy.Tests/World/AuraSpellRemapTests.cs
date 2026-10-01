using System.IO;

using HermesProxy.World;

using Xunit;

namespace HermesProxy.Tests.World;

/// <summary>
/// Issue #330 — the 3.3.5a Prayer of Mending rank 2/3 auras (48110, 48111) sit on aura type 225 in
/// the 3.4.3 Spell DB2, which that client reads as SPELL_AURA_MOD_VISIBILITY_RANGE. Forwarded
/// as-is, whoever carries one stops seeing every other unit. The table is data
/// (CSV/AuraSpellRemap3.csv); these tests read it through the parser so they do not depend on
/// which expansion the process loaded.
/// </summary>
public class AuraSpellRemapTests
{
    private const uint PrayerOfMendingAura = 41635;
    private const uint PrayerOfMendingAuraRank2 = 48110;
    private const uint PrayerOfMendingAuraRank3 = 48111;

    private static (System.Collections.Frozen.FrozenDictionary<uint, uint> LegacyToModern,
        System.Collections.Frozen.FrozenDictionary<uint, uint[]> ModernToLegacy) LoadShipped()
        => AuraSpellRemap.Parse(Path.Combine("CSV", "AuraSpellRemap3.csv"));

    [Theory]
    [InlineData(PrayerOfMendingAuraRank2)]
    [InlineData(PrayerOfMendingAuraRank3)]
    public void ShippedTable_PrayerOfMendingRankAura_IsShownAsTheRank1Aura(uint legacySpellId)
    {
        Assert.Equal(PrayerOfMendingAura, LoadShipped().LegacyToModern[legacySpellId]);
    }

    [Theory]
    [InlineData(PrayerOfMendingAura)]
    [InlineData(33076u)]  // Prayer of Mending rank 1 cast
    [InlineData(48112u)]  // Prayer of Mending rank 2 cast
    [InlineData(48113u)]  // Prayer of Mending rank 3 cast
    [InlineData(41637u)]  // Prayer of Mending heal
    [InlineData(48066u)]  // Power Word: Shield
    public void ShippedTable_OtherSpell_HasNoRemap(uint spellId)
    {
        Assert.False(LoadShipped().LegacyToModern.ContainsKey(spellId));
    }

    [Fact]
    public void ShippedTable_CancellingPrayerOfMending_ReachesEveryLegacyRankAura()
    {
        Assert.Equal(
            [PrayerOfMendingAura, PrayerOfMendingAuraRank2, PrayerOfMendingAuraRank3],
            LoadShipped().ModernToLegacy[PrayerOfMendingAura]);
    }

    [Fact]
    public void ShippedTable_UnmappedAura_HasNoReverseEntry()
    {
        Assert.False(LoadShipped().ModernToLegacy.ContainsKey(48066u));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Parse_ExpansionWithoutAFile_LeavesBothTablesEmpty(int expansion)
    {
        var (legacyToModern, modernToLegacy) =
            AuraSpellRemap.Parse(Path.Combine("CSV", $"AuraSpellRemap{expansion}.csv"));

        Assert.Empty(legacyToModern);
        Assert.Empty(modernToLegacy);
    }

    [Fact]
    public void Parse_MalformedRows_AreSkipped()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path,
            [
                "LegacyId,ModernId,Comment",
                "48110,41635,kept",
                "abc,41635,not a number",
                "0,41635,zero legacy id",
                "48111,0,zero modern id",
                "7,7,maps to itself",
                "9,10,kept",
            ]);

            var (legacyToModern, modernToLegacy) = AuraSpellRemap.Parse(path);

            Assert.Equal(2, legacyToModern.Count);
            Assert.Equal(41635u, legacyToModern[48110]);
            Assert.Equal(10u, legacyToModern[9]);
            Assert.Equal([10u, 9u], modernToLegacy[10]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
