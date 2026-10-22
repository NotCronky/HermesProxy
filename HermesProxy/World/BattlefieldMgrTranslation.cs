using System;

namespace HermesProxy.World;

internal enum BattlefieldMgrTicketKind : byte
{
    Entry = 0,
    Queue = 1,
}

internal static class BattlefieldMgrTranslation
{
    public const uint WintergraspBattleId = 1;
    public const uint WintergraspZoneId = 4197;
    public const uint ModernWintergraspListId = 1089;
    public const uint LegacyWintergraspMapId = 571;
    public const uint DefaultInviteTimeoutMs = 20_000;
    public const byte MinLevel = 71;
    public const byte MaxLevel = 80;

    // Tol Barad, the battlefield a 4.3.4 server adds (TrinityCore 4.3.4 BATTLEFIELD_BATTLEID_TB, its
    // zone and map; the 4.4.2 client's BattlemasterList row "Battle for Tol Barad", levels 85 to 85).
    public const uint TolBaradBattleId = 21;
    public const uint TolBaradZoneId = 5095;
    public const uint ModernTolBaradListId = 1116;
    public const uint LegacyTolBaradMapId = 732;
    public const byte TolBaradLevel = 85;

    // The 4.4.2 client's Wintergrasp row runs to 85, the level cap of a 4.3.4 server.
    public const byte CataMaxLevel = 85;

    public static ReadOnlySpan<uint> Battles => [WintergraspBattleId, TolBaradBattleId];

    public const uint EntryTicketBase = 100;
    public const uint QueueTicketBase = 200;

    public static uint TicketFor(uint battleId, BattlefieldMgrTicketKind kind) =>
        (kind == BattlefieldMgrTicketKind.Queue ? QueueTicketBase : EntryTicketBase) + battleId;

    public static bool TryDecodeTicket(uint ticketId, out uint battleId, out BattlefieldMgrTicketKind kind)
    {
        if (ticketId > QueueTicketBase)
        {
            battleId = ticketId - QueueTicketBase;
            kind = BattlefieldMgrTicketKind.Queue;
            return battleId != 0;
        }

        if (ticketId > EntryTicketBase && ticketId < QueueTicketBase)
        {
            battleId = ticketId - EntryTicketBase;
            kind = BattlefieldMgrTicketKind.Entry;
            return battleId != 0;
        }

        battleId = 0;
        kind = BattlefieldMgrTicketKind.Entry;
        return false;
    }

    public static uint ListIdForBattle(uint battleId) => battleId switch
    {
        WintergraspBattleId => ModernWintergraspListId,
        TolBaradBattleId => ModernTolBaradListId,
        _ => battleId,
    };

    public static uint MapIdForBattle(uint battleId) =>
        battleId == TolBaradBattleId ? LegacyTolBaradMapId : LegacyWintergraspMapId;

    public static uint ZoneIdForBattle(uint battleId) =>
        battleId == TolBaradBattleId ? TolBaradZoneId : WintergraspZoneId;

    /// <summary>The battle a zone belongs to, 0 for a zone without one.</summary>
    public static uint BattleIdForZone(uint zoneId) => zoneId switch
    {
        WintergraspZoneId => WintergraspBattleId,
        TolBaradZoneId => TolBaradBattleId,
        _ => 0,
    };

    /// <summary>The battle fought on a map, 0 for a map without one.</summary>
    public static uint BattleIdForMap(uint mapId) => mapId switch
    {
        LegacyWintergraspMapId => WintergraspBattleId,
        LegacyTolBaradMapId => TolBaradBattleId,
        _ => 0,
    };

    public static (byte Min, byte Max) LevelsForBattle(uint battleId, bool cataLegacy) =>
        battleId == TolBaradBattleId ? (TolBaradLevel, TolBaradLevel) : (MinLevel, cataLegacy ? CataMaxLevel : MaxLevel);

    public static bool ShouldRouteLeaveToMgr(uint currentZoneId, bool hasTicket) =>
        hasTicket && BattleIdForZone(currentZoneId) != 0;

    public static uint TimeoutMs(uint expireUnix, long nowUnix)
    {
        if (expireUnix <= (uint)nowUnix)
            return DefaultInviteTimeoutMs;
        ulong ms = (ulong)(expireUnix - (uint)nowUnix) * 1000UL;
        return ms > uint.MaxValue ? uint.MaxValue : (uint)ms;
    }
}
