using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

using Framework.Logging;

using nietras.SeparatedValues;

namespace HermesProxy.World;

/// <summary>
/// Rewrites legacy aura spell ids whose row in the modern client's Spell DB2 now means
/// something else, so the client does not apply an effect the server never cast.
/// </summary>
/// <remarks>
/// The modern client applies some aura effects locally from its own spell data. 3.3.5a
/// Prayer of Mending puts a per-rank aura on its target - 41635, 48110, 48111 - all aura type
/// 225, SPELL_AURA_RAID_PROC_FROM_CHARGE_WITH_VALUE to the 3.3.5a engine. WotLK Classic rebuilt
/// the spell around 41635 (Apply Aura: Dummy) for every rank and left 48110 and 48111 in its
/// Spell DB2 on aura type 225, which its engine reads as SPELL_AURA_MOD_VISIBILITY_RANGE.
/// Forwarded as-is, whoever carries one stops seeing every other unit and drops out of everyone
/// else's view until the aura moves on. Issue #330.
/// <para>
/// The rows live in <c>CSV/AuraSpellRemap{expansion}.csv</c>, so a remap needs no code and only
/// a client whose spell data diverged loads any; for the rest both tables stay empty and every
/// id passes through, which is why callers need no build check.
/// </para>
/// </remarks>
public static class AuraSpellRemap
{
    private static readonly Microsoft.Extensions.Logging.ILogger _melStorage = Log.CreateMelLogger(Log.CategoryStorage);
    private static readonly string _sourceFile = nameof(AuraSpellRemap).PadRight(15);
    private const string _netDirNone = "";

    /// <summary>Legacy aura spell id -> the id the modern client is shown instead.</summary>
    public static FrozenDictionary<uint, uint> LegacyToModern { get; private set; } = FrozenDictionary<uint, uint>.Empty;

    /// <summary>
    /// Modern aura spell id -> every legacy id that is shown to the client as it, the modern id
    /// first. A CMSG_CANCEL_AURA names only the id the client saw, so it cannot say which of them
    /// it means.
    /// </summary>
    public static FrozenDictionary<uint, uint[]> ModernToLegacy { get; private set; } = FrozenDictionary<uint, uint[]>.Empty;

    /// <summary>
    /// The aura spell id to show the modern client in place of <paramref name="legacySpellId"/>,
    /// or <paramref name="legacySpellId"/> unchanged when no remap is known.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToModern(uint legacySpellId)
        => LegacyToModern.TryGetValue(legacySpellId, out uint modernSpellId) ? modernSpellId : legacySpellId;

    /// <summary>
    /// The legacy aura ids a modern <paramref name="modernSpellId"/> may stand for, when it is the
    /// target of a remap.
    /// </summary>
    public static bool TryGetLegacyIds(uint modernSpellId, [NotNullWhen(true)] out uint[]? legacySpellIds)
        => ModernToLegacy.TryGetValue(modernSpellId, out legacySpellIds);

    public static void Load()
    {
        (LegacyToModern, ModernToLegacy) =
            Parse(Path.Combine("CSV", $"AuraSpellRemap{ModernVersion.ExpansionVersion}.csv"));
    }

    /// <summary>
    /// Reads one AuraSpellRemap CSV into both directions. Split from <see cref="Load"/> so a test
    /// can read the expansion-3 file without the process being built for that expansion.
    /// </summary>
    public static (FrozenDictionary<uint, uint> LegacyToModern, FrozenDictionary<uint, uint[]> ModernToLegacy) Parse(string path)
    {
        if (!File.Exists(path))
            return (FrozenDictionary<uint, uint>.Empty, FrozenDictionary<uint, uint[]>.Empty);

        using var reader = Sep.Reader(o => o with { HasHeader = true }).FromFile(path);
        var legacyToModern = new Dictionary<uint, uint>();
        int rowNumber = 1;

        foreach (var row in reader)
        {
            rowNumber++;
            if (!uint.TryParse(row[0].Span, out uint legacyId) ||
                !uint.TryParse(row[1].Span, out uint modernId) ||
                legacyId == 0 || modernId == 0 || legacyId == modernId)
            {
                GameDataLogMessages.MalformedCsvRow(_melStorage, _sourceFile, _netDirNone, rowNumber, path);
                continue;
            }
            legacyToModern[legacyId] = modernId;
        }

        // The modern id leads its list because it is normally a legacy aura in its own right
        // (41635 is the rank 1 Prayer of Mending aura on both sides); the server ignores a cancel
        // for an aura the player does not carry, so sending every candidate is safe.
        var modernToLegacy = new Dictionary<uint, List<uint>>();
        foreach (var (legacyId, modernId) in legacyToModern)
        {
            if (!modernToLegacy.TryGetValue(modernId, out var legacyIds))
                modernToLegacy[modernId] = legacyIds = [];
            legacyIds.Add(legacyId);
        }

        var reverse = new Dictionary<uint, uint[]>(modernToLegacy.Count);
        foreach (var (modernId, legacyIds) in modernToLegacy)
        {
            legacyIds.Sort();
            reverse[modernId] = [modernId, .. legacyIds];
        }

        return (legacyToModern.ToFrozenDictionary(), reverse.ToFrozenDictionary());
    }
}
