using System;
using HermesProxy.World;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Logging;
using HermesProxy.World.Outbox;
using HermesProxy.World.Server.Packets;

namespace HermesProxy.World.Client;

public partial class WorldClient
{
    [HandlesSmsg(Opcode.SMSG_BATTLEFIELD_MGR_ENTRY_INVITE)]
    internal void HandleBattlefieldMgrEntryInvite(WorldPacket packet)
    {
        uint battleId, zoneId, expireUnix;
        if (IsCataLegacy)
        {
            // TrinityCore 4.3.4 SendBfInvitePlayerToWar: the battle's GUID, its zone, and when the
            // invitation runs out
            Span<bool> mask = stackalloc bool[8];
            Span<byte> guid = stackalloc byte[8];
            MaskedGuid.ReadMaskBits(packet, mask, [5, 3, 7, 2, 6, 4, 1, 0]);
            MaskedGuid.ReadBytes(packet, mask, guid, [6]);
            zoneId = packet.ReadUInt32();
            MaskedGuid.ReadBytes(packet, mask, guid, [1, 3, 4, 2, 0]);
            expireUnix = packet.ReadUInt32();
            MaskedGuid.ReadBytes(packet, mask, guid, [7, 5]);
            battleId = CataBattleId(MaskedGuid.ToUInt64(guid), zoneId);
        }
        else
        {
            battleId = packet.ReadUInt32();
            zoneId = packet.ReadUInt32();
            expireUnix = packet.ReadUInt32();
        }

        uint ticketId = BattlefieldMgrTranslation.TicketFor(battleId, BattlefieldMgrTicketKind.Entry);
        uint listId = BattlefieldMgrTranslation.ListIdForBattle(battleId);
        uint timeoutMs = BattlefieldMgrTranslation.TimeoutMs(expireUnix, Time.UnixTime);

        // The queue is done with once the battle invites; left alone, the 4.4.2 client went on
        // listing it as queued beside the battle it had entered
        if (IsCataLegacy)
            DropBattlefieldMgrQueueTicket(battleId);

        GetSession().GameState.StoreBattleFieldQueueType(ticketId, listId);

        var confirm = new BattlefieldStatusNeedConfirmation();
        FillMgrHeader(confirm.Hdr, battleId, ticketId, listId);
        confirm.Mapid = BattlefieldMgrTranslation.MapIdForBattle(battleId);
        confirm.Timeout = timeoutMs;

        BattleGroundLogMessages.EntryInvite(
            _melLog, "ENTRY_INVITE", battleId, zoneId, expireUnix, ticketId, listId, timeoutMs);
        SendPacketToClient(confirm);

        // TrinityCore 4.3.4 lets an unanswered invitation run out without a word (Battlefield::Update
        // only teleports a player who is in the zone), and the 4.4.2 client keeps its "enter battle"
        // prompt until the ticket's status changes
        if (IsCataLegacy)
            ArmBattlefieldMgrInviteExpiry(ticketId, timeoutMs);
    }

    internal static HoldKey BattlefieldInviteExpiryKey(uint ticketId)
        => new(HoldKeyKind.BattlefieldInviteExpiry, ticketId);

    void ArmBattlefieldMgrInviteExpiry(uint ticketId, uint timeoutMs)
    {
        var toClient = GetSession().ToClient;
        var key = BattlefieldInviteExpiryKey(ticketId);
        toClient.Cancel(key);
        toClient.Delay(TimeSpan.FromMilliseconds(timeoutMs), () => ExpireBattlefieldMgrInvite(ticketId),
            new HoldOptions(Key: key));
    }

    internal void ExpireBattlefieldMgrInvite(uint ticketId)
    {
        if (GetSession().GameState.GetBattleFieldQueueType(ticketId) == 0)
            return;

        BattleGroundLogMessages.EntryInviteExpired(_melLog, ticketId);
        DropBattlefieldMgrTicket(ticketId);
    }

    [HandlesSmsg(Opcode.SMSG_BATTLEFIELD_MGR_QUEUE_INVITE)]
    internal void HandleBattlefieldMgrQueueInvite(WorldPacket packet)
    {
        uint battleId;
        byte warmup = 0;
        if (IsCataLegacy)
        {
            // WowPacketParser 4.3.4: the GUID's mask among bits that each leave out an optional
            // field (TrinityCore 4.3.4 SendBfInvitePlayerToQueue sends only the warmup byte), then
            // the bytes with the optional fields among them
            Span<bool> mask = stackalloc bool[8];
            Span<byte> guid = stackalloc byte[8];
            bool hasInt1 = !packet.HasBit();
            bool hasWarmup = !packet.HasBit();
            bool hasInt2 = !packet.HasBit();
            MaskedGuid.ReadMaskBits(packet, mask, [0]);
            bool hasInt3 = !packet.HasBit();
            MaskedGuid.ReadMaskBits(packet, mask, [2, 6, 3]);
            bool hasInt4 = !packet.HasBit();
            packet.HasBit();
            MaskedGuid.ReadMaskBits(packet, mask, [1, 5, 4]);
            bool hasInt5 = !packet.HasBit();
            MaskedGuid.ReadMaskBits(packet, mask, [7]);

            MaskedGuid.ReadBytes(packet, mask, guid, [2]);
            if (hasInt2)
                packet.ReadInt32();
            MaskedGuid.ReadBytes(packet, mask, guid, [3, 6]);
            if (hasWarmup)
                warmup = packet.ReadUInt8();
            MaskedGuid.ReadBytes(packet, mask, guid, [5, 0]);
            if (hasInt1)
                packet.ReadInt32();
            if (hasInt5)
                packet.ReadInt32();
            if (hasInt3)
                packet.ReadInt32();
            MaskedGuid.ReadBytes(packet, mask, guid, [4]);
            if (hasInt4)
                packet.ReadInt32();
            MaskedGuid.ReadBytes(packet, mask, guid, [1, 7]);
            battleId = CataBattleId(MaskedGuid.ToUInt64(guid));
        }
        else
        {
            battleId = packet.ReadUInt32();
            warmup = packet.ReadUInt8();
        }

        uint ticketId = BattlefieldMgrTranslation.TicketFor(battleId, BattlefieldMgrTicketKind.Queue);
        uint listId = BattlefieldMgrTranslation.ListIdForBattle(battleId);
        GetSession().GameState.StoreBattleFieldQueueType(ticketId, listId);

        var confirm = new BattlefieldStatusNeedConfirmation();
        FillMgrHeader(confirm.Hdr, battleId, ticketId, listId);
        confirm.Mapid = BattlefieldMgrTranslation.MapIdForBattle(battleId);
        confirm.Timeout = BattlefieldMgrTranslation.DefaultInviteTimeoutMs;

        BattleGroundLogMessages.QueueInvite(_melLog, "QUEUE_INVITE", battleId, warmup, ticketId);
        SendPacketToClient(confirm);
    }

    [HandlesSmsg(Opcode.SMSG_BATTLEFIELD_MGR_QUEUE_REQUEST_RESPONSE)]
    internal void HandleBattlefieldMgrQueueRequestResponse(WorldPacket packet)
    {
        uint battleId;
        byte canQueue, loggingIn, warmup;
        if (IsCataLegacy)
        {
            // TrinityCore 4.3.4 SendBfQueueInviteResponse, with WowPacketParser 4.3.4's second GUID
            // (TrinityCore never sends one)
            Span<bool> mask = stackalloc bool[8];
            Span<bool> secondMask = stackalloc bool[8];
            Span<byte> guid = stackalloc byte[8];
            Span<byte> second = stackalloc byte[8];
            MaskedGuid.ReadMaskBits(packet, mask, [1, 6, 5, 7]);
            loggingIn = packet.HasBit() ? (byte)1 : (byte)0;
            MaskedGuid.ReadMaskBits(packet, mask, [0]);
            bool hasSecondGuid = !packet.HasBit();
            MaskedGuid.ReadMaskBits(packet, mask, [4]);
            if (hasSecondGuid)
                MaskedGuid.ReadMaskBits(packet, secondMask, [7, 3, 0, 4, 2, 6, 1, 5]);
            MaskedGuid.ReadMaskBits(packet, mask, [3, 2]);
            if (hasSecondGuid)
                MaskedGuid.ReadBytes(packet, secondMask, second, [2, 5, 3, 0, 4, 6, 1, 7]);
            canQueue = packet.ReadUInt8();
            MaskedGuid.ReadBytes(packet, mask, guid, [1, 3, 6, 7, 0]);
            warmup = packet.ReadUInt8();
            MaskedGuid.ReadBytes(packet, mask, guid, [2, 4, 5]);
            uint zoneId = packet.ReadUInt32();
            battleId = CataBattleId(MaskedGuid.ToUInt64(guid), zoneId);
        }
        else
        {
            battleId = packet.ReadUInt32();
            packet.ReadUInt32();
            canQueue = packet.ReadUInt8();
            loggingIn = packet.ReadUInt8();
            warmup = packet.ReadUInt8();
        }

        uint ticketId = BattlefieldMgrTranslation.TicketFor(battleId, BattlefieldMgrTicketKind.Queue);
        uint listId = BattlefieldMgrTranslation.ListIdForBattle(battleId);
        GetSession().GameState.StoreBattleFieldQueueType(ticketId, listId);

        BattleGroundLogMessages.QueueResponse(
            _melLog, "QUEUE_RESPONSE", battleId, canQueue, loggingIn, warmup);

        if (canQueue == 0)
        {
            SendMgrFailed(ticketId, listId);
            GetSession().GameState.RemoveBattleFieldQueue(ticketId);
            return;
        }

        var queued = new BattlefieldStatusQueued();
        FillMgrHeader(queued.Hdr, battleId, ticketId, listId);
        queued.EligibleForMatchmaking = true;
        SendPacketToClient(queued);
    }

    [HandlesSmsg(Opcode.SMSG_BATTLEFIELD_MGR_ENTERING)]
    internal void HandleBattlefieldMgrEntering(WorldPacket packet)
    {
        uint battleId;
        if (IsCataLegacy)
        {
            // TrinityCore 4.3.4 SendBfEntered (SMSG_BATTLEFIELD_MGR_ENTERED): three flags among the
            // GUID's mask (one of them whether to clear AFK)
            Span<bool> mask = stackalloc bool[8];
            Span<byte> guid = stackalloc byte[8];
            packet.HasBit();
            packet.HasBit();
            MaskedGuid.ReadMaskBits(packet, mask, [1, 4, 5, 0, 3]);
            packet.HasBit();
            MaskedGuid.ReadMaskBits(packet, mask, [6, 7, 2]);
            MaskedGuid.ReadBytes(packet, mask, guid, [5, 3, 0, 4, 1, 7, 2, 6]);
            battleId = CataBattleId(MaskedGuid.ToUInt64(guid));
        }
        else
        {
            battleId = packet.ReadUInt32();
            packet.ReadUInt8();
            packet.ReadUInt8();
            packet.ReadUInt8();
        }

        uint ticketId = BattlefieldMgrTranslation.TicketFor(battleId, BattlefieldMgrTicketKind.Entry);
        uint listId = BattlefieldMgrTranslation.ListIdForBattle(battleId);
        uint mapId = BattlefieldMgrTranslation.MapIdForBattle(battleId);
        GetSession().ToClient.Cancel(BattlefieldInviteExpiryKey(ticketId));
        GetSession().GameState.StoreBattleFieldQueueType(ticketId, listId);

        var active = new BattlefieldStatusActive();
        FillMgrHeader(active.Hdr, battleId, ticketId, listId);
        active.Mapid = mapId;

        BattleGroundLogMessages.Entered(_melLog, "ENTERING", battleId, mapId);
        SendPacketToClient(active);
    }

    [HandlesSmsg(Opcode.SMSG_BATTLEFIELD_MGR_EJECTED)]
    internal void HandleBattlefieldMgrEjected(WorldPacket packet)
    {
        uint battleId;
        byte reason, status, relocated;
        if (IsCataLegacy)
        {
            // TrinityCore 4.3.4 SendBfLeaveMessage
            Span<bool> mask = stackalloc bool[8];
            Span<byte> guid = stackalloc byte[8];
            MaskedGuid.ReadMaskBits(packet, mask, [2, 5, 1, 0, 3, 6]);
            relocated = packet.HasBit() ? (byte)1 : (byte)0;
            MaskedGuid.ReadMaskBits(packet, mask, [7, 4]);
            status = packet.ReadUInt8();
            MaskedGuid.ReadBytes(packet, mask, guid, [1, 7, 4, 2, 3]);
            reason = packet.ReadUInt8();
            MaskedGuid.ReadBytes(packet, mask, guid, [6, 0, 5]);
            battleId = CataBattleId(MaskedGuid.ToUInt64(guid));
        }
        else
        {
            battleId = packet.ReadUInt32();
            reason = packet.ReadUInt8();
            status = packet.ReadUInt8();
            relocated = packet.ReadUInt8();
        }

        BattleGroundLogMessages.Ejected(_melLog, "EJECTED", battleId, reason, status, relocated);
        ClearBattlefieldMgrState(battleId, always: true);
    }

    void FillMgrHeader(BattlefieldStatusHeader hdr, uint battleId, uint ticketId, uint listId)
    {
        var (minLevel, maxLevel) = BattlefieldMgrTranslation.LevelsForBattle(battleId, IsCataLegacy);
        hdr.Ticket.Id = ticketId;
        hdr.Ticket.RequesterGuid = GetSession().GameState.CurrentPlayerGuid;
        hdr.Ticket.Time = GetSession().GameState.GetBattleFieldQueueTime(ticketId);
        hdr.Ticket.Type = RideType.Battlegrounds;
        hdr.BattlefieldListIDs.Add(listId);
        hdr.RangeMin = minLevel;
        hdr.RangeMax = maxLevel;
    }

    /// <summary>
    /// The battle a 4.3.4 battlefield packet is about. TrinityCore 4.3.4 never sets a battlefield's
    /// GUID (Battlefield::m_Guid stays empty), so the battle is the one the packet's zone belongs
    /// to, else the one this session holds a ticket for, else the one on the player's map. The map
    /// before the zone: the queue invitation comes on entering the zone, ahead of the world states
    /// that tell the proxy the new zone (taken for Wintergrasp, it offered "Northrend" in Tol
    /// Barad). The GUID is kept for the client's answers.
    /// </summary>
    uint CataBattleId(ulong guid, uint zoneId = 0)
    {
        var gameState = GetSession().GameState;
        uint battleId = BattlefieldMgrTranslation.BattleIdForZone(zoneId);
        if (battleId == 0)
        {
            foreach (uint battle in BattlefieldMgrTranslation.Battles)
            {
                if (HasBattlefieldMgrTicket(battle))
                {
                    battleId = battle;
                    break;
                }
            }
        }
        if (battleId == 0 && gameState.CurrentMapId is uint mapId)
            battleId = BattlefieldMgrTranslation.BattleIdForMap(mapId);
        if (battleId == 0)
            battleId = BattlefieldMgrTranslation.BattleIdForZone(gameState.CurrentZoneId);
        if (battleId == 0)
            battleId = BattlefieldMgrTranslation.WintergraspBattleId;

        gameState.BattlefieldMgrGuids[battleId] = guid;
        return battleId;
    }

    internal bool HasBattlefieldMgrTicket(uint battleId)
    {
        var gameState = GetSession().GameState;
        return gameState.GetBattleFieldQueueType(BattlefieldMgrTranslation.TicketFor(battleId, BattlefieldMgrTicketKind.Entry)) != 0 ||
            gameState.GetBattleFieldQueueType(BattlefieldMgrTranslation.TicketFor(battleId, BattlefieldMgrTicketKind.Queue)) != 0;
    }

    /// <summary>
    /// Takes the battle's entries off the client's queue list. Leaving its zone does it only for a
    /// ticket the session holds; an ejection from the server does it whatever the session knows.
    /// </summary>
    internal void ClearBattlefieldMgrState(uint battleId, bool always = false)
    {
        uint entryTicket = BattlefieldMgrTranslation.TicketFor(battleId, BattlefieldMgrTicketKind.Entry);
        uint queueTicket = BattlefieldMgrTranslation.TicketFor(battleId, BattlefieldMgrTicketKind.Queue);
        uint listId = BattlefieldMgrTranslation.ListIdForBattle(battleId);

        GetSession().ToClient.Cancel(BattlefieldInviteExpiryKey(entryTicket));
        ClearBattlefieldMgrTicket(entryTicket, listId, always);
        ClearBattlefieldMgrTicket(queueTicket, listId, always);
    }

    // Before the map change, not after: once the 4.4.2 client has changed maps the ticket is gone
    // from its list without an UPDATE_BATTLEFIELD_STATUS for its slot, a STATUS_NONE then fires the
    // event for slot 0, and the "Enter Battle" prompt of the old slot stays on the screen
    // (2026-10-20, Tol Barad to Stormwind with an invitation open)
    internal void ClearBattlefieldMgrStateLeavingFor(uint newMapId)
    {
        foreach (uint battle in BattlefieldMgrTranslation.Battles)
        {
            if (BattlefieldMgrTranslation.MapIdForBattle(battle) != newMapId && HasBattlefieldMgrTicket(battle))
                ClearBattlefieldMgrState(battle);
        }
    }

    // On 4.3.4 a ticket the session holds goes with STATUS_NONE, which the 4.4.2 client's prompt
    // closes on without a failure line; the 3.3.5a path keeps STATUS_FAILED
    void ClearBattlefieldMgrTicket(uint ticketId, uint listId, bool always)
    {
        bool held = GetSession().GameState.GetBattleFieldQueueType(ticketId) != 0;
        if (held && IsCataLegacy)
            DropBattlefieldMgrTicket(ticketId);
        else if (held || always)
        {
            SendMgrFailed(ticketId, listId);
            GetSession().GameState.RemoveBattleFieldQueue(ticketId);
        }
    }

    void DropBattlefieldMgrQueueTicket(uint battleId)
        => DropBattlefieldMgrTicket(BattlefieldMgrTranslation.TicketFor(battleId, BattlefieldMgrTicketKind.Queue));

    void DropBattlefieldMgrTicket(uint ticketId)
    {
        var gameState = GetSession().GameState;
        if (gameState.GetBattleFieldQueueType(ticketId) == 0)
            return;

        var none = new BattlefieldStatusNone();
        none.Ticket.Id = ticketId;
        none.Ticket.RequesterGuid = gameState.CurrentPlayerGuid;
        none.Ticket.Time = gameState.GetBattleFieldQueueTime(ticketId);
        none.Ticket.Type = RideType.Battlegrounds;
        SendPacketToClient(none);
        gameState.RemoveBattleFieldQueue(ticketId);
    }

    void SendMgrFailed(uint ticketId, uint listId)
    {
        var failed = new BattlefieldStatusFailed();
        failed.Ticket.Id = ticketId;
        failed.Ticket.RequesterGuid = GetSession().GameState.CurrentPlayerGuid;
        failed.Ticket.Time = GetSession().GameState.GetBattleFieldQueueTime(ticketId);
        failed.Ticket.Type = RideType.Battlegrounds;
        failed.BattlefieldListId = listId;
        failed.Reason = 30;
        SendPacketToClient(failed);
    }
}
