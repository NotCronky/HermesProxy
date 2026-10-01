using HermesProxy.World.Enums;

namespace HermesProxy.World;

/// <summary>
/// Rewrites legacy aura spell ids whose row in the modern client's Spell DB2 now means
/// something else, so the client does not apply an effect the server never cast.
///
/// The modern client applies some aura effects locally from its own spell data. 3.3.5a
/// Prayer of Mending puts a per-rank aura on its target — 41635, 48110, 48111 — all
/// SPELL_AURA_RAID_PROC_FROM_CHARGE_WITH_VALUE (225). The 3.4.3 client only uses 41635
/// (Apply Aura: Dummy) for every rank; 48110 and 48111 survive in its Spell DB2 with aura
/// type 225 read as SPELL_AURA_MOD_VISIBILITY_RANGE and a tiny amount (101 on 48110).
/// Forwarding them as-is shrinks the player's visibility range for as long as Prayer of
/// Mending sits on them: every other player and creature vanishes while spell visuals keep
/// drawing, until the next hit bounces it away. Issue #330.
///
/// Deliberately V3_4_3-only; callers gate on the build. The rank auras are fine on clients
/// whose Spell DB2 still matches the legacy one.
/// </summary>
public static class AuraSpellRemap
{
    private static readonly uint[] PrayerOfMendingLegacyAuras =
    [
        KnownSpellIds.PrayerOfMendingAura,
        KnownSpellIds.PrayerOfMendingAuraRank2,
        KnownSpellIds.PrayerOfMendingAuraRank3,
    ];

    /// <summary>
    /// The aura spell id to show the modern client in place of <paramref name="legacySpellId"/>,
    /// or <paramref name="legacySpellId"/> unchanged when no remap is known.
    /// </summary>
    public static uint ToModern(uint legacySpellId) => legacySpellId switch
    {
        KnownSpellIds.PrayerOfMendingAuraRank2 or
        KnownSpellIds.PrayerOfMendingAuraRank3 => KnownSpellIds.PrayerOfMendingAura,
        _ => legacySpellId,
    };

    /// <summary>
    /// Legacy aura ids a CMSG_CANCEL_AURA for <paramref name="modernSpellId"/> may stand for.
    /// The client only ever saw the remapped id, so it cannot say which rank it is cancelling;
    /// the server ignores a cancel for an aura the player does not have.
    /// </summary>
    public static uint[] ToLegacyCancelCandidates(uint modernSpellId)
    {
        if (modernSpellId == KnownSpellIds.PrayerOfMendingAura)
            return PrayerOfMendingLegacyAuras;
        return [modernSpellId];
    }
}
