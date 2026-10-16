using System.Collections.Generic;
using HermesProxy.World.Enums;

namespace HermesProxy.World.Server.Packets;

// Calendar dates are packed dates on both sides, with the same bits: they are carried as the
// number they came as and never turned into a time of this machine's zone on the way.

/// <summary>
/// How many calendar invitations wait for an answer: the number on the minimap's calendar button.
/// </summary>
public sealed class CalendarSendNumPending : ServerPacket
{
    public CalendarSendNumPending() : base(Opcode.SMSG_CALENDAR_SEND_NUM_PENDING) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(NumPending);
    }

    public uint NumPending;
}

public readonly record struct CalendarGetEvent(ulong EventId);

public readonly record struct CalendarAddEventInvite(WowGuid128 Guid, byte Status, byte Moderator);

public readonly record struct CalendarAddEvent(
    byte EventType,
    int TextureId,
    uint Time,
    uint Flags,
    CalendarAddEventInvite[] Invites,
    string Title,
    string Description);

public readonly record struct CalendarUpdateEvent(
    ulong EventId,
    ulong ModeratorId,
    byte EventType,
    int TextureId,
    uint Time,
    uint Flags,
    string Title,
    string Description);

public readonly record struct CalendarRemoveEvent(ulong EventId, ulong ModeratorId, uint Flags);

public readonly record struct CalendarCopyEvent(ulong EventId, ulong ModeratorId, uint Date);

public readonly record struct CalendarInvite(ulong EventId, ulong ModeratorId, string Name, bool Creating, bool IsSignUp);

public readonly record struct CalendarEventSignUp(ulong EventId, bool Tentative);

public readonly record struct CalendarRsvp(ulong EventId, ulong InviteId, byte Status);

public readonly record struct CalendarRemoveInvite(WowGuid128 Guid, ulong InviteId, ulong ModeratorId, ulong EventId);

/// <summary>An invitee's answer or rank set by a moderator: the two requests share a layout.</summary>
public readonly record struct CalendarInviteeStatus(WowGuid128 Guid, ulong EventId, ulong InviteId, ulong ModeratorId, byte Status);

public readonly record struct CalendarComplain(WowGuid128 InvitedByGuid, ulong EventId, ulong InviteId);

public readonly record struct CalendarCommunityInviteRequest(ulong ClubId, byte MinLevel, byte MaxLevel, byte MaxRankOrder);

/// <summary>
/// The calendar's own list: the invitations and events of the player and the raids the player is
/// saved to. The days raids reset on are sent empty, as TrinityCore wotlk_classic sends them.
/// </summary>
/// <remarks>
/// Four counts first, then the four lists, resets before lockouts (the 3.4.3 client's layout;
/// a 3.3.5a server puts each count in front of its own list and the lockouts first).
/// </remarks>
public sealed class CalendarSendCalendar : ServerPacket
{
    public CalendarSendCalendar() : base(Opcode.SMSG_CALENDAR_SEND_CALENDAR) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(ServerTime);
        _worldPacket.WriteInt32(Invites.Count);
        _worldPacket.WriteInt32(Events.Count);
        _worldPacket.WriteInt32(RaidLockouts.Count);
        _worldPacket.WriteInt32(0);     // raid resets

        foreach (CalendarRaidLockout lockout in RaidLockouts)
        {
            _worldPacket.WriteUInt64(lockout.InstanceId);
            _worldPacket.WriteInt32(lockout.MapId);
            _worldPacket.WriteUInt32(lockout.DifficultyId);
            _worldPacket.WriteInt32(lockout.ExpireTime);
        }

        foreach (CalendarListInvite invite in Invites)
        {
            _worldPacket.WriteUInt64(invite.EventId);
            _worldPacket.WriteUInt64(invite.InviteId);
            _worldPacket.WriteUInt8(invite.Status);
            _worldPacket.WriteUInt8(invite.Moderator);
            _worldPacket.WriteUInt8(invite.InviteType);
            _worldPacket.WritePackedGuid128(invite.InviterGuid);
            _worldPacket.WriteBit(false);   // ignores the friend and guild restriction
            _worldPacket.FlushBits();
        }

        foreach (CalendarListEvent calendarEvent in Events)
        {
            _worldPacket.WriteUInt64(calendarEvent.EventId);
            _worldPacket.WriteUInt8(calendarEvent.EventType);
            _worldPacket.WriteUInt32(calendarEvent.Date);
            _worldPacket.WriteUInt32(calendarEvent.Flags);
            _worldPacket.WriteInt32(calendarEvent.TextureId);
            _worldPacket.WriteUInt64(calendarEvent.EventClubId);
            _worldPacket.WritePackedGuid128(calendarEvent.OwnerGuid);
            _worldPacket.WriteBits(System.Text.Encoding.UTF8.GetByteCount(calendarEvent.EventName), 8);
            _worldPacket.FlushBits();
            _worldPacket.WriteString(calendarEvent.EventName);
        }
    }

    public uint ServerTime;
    public List<CalendarListInvite> Invites = [];
    public List<CalendarListEvent> Events = [];
    public List<CalendarRaidLockout> RaidLockouts = [];
}

public struct CalendarListInvite
{
    public ulong EventId;
    public ulong InviteId;
    public byte Status;
    public byte Moderator;
    public byte InviteType;
    public WowGuid128 InviterGuid;
}

public struct CalendarListEvent
{
    public ulong EventId;
    public byte EventType;
    public uint Date;
    public uint Flags;
    public int TextureId;
    public ulong EventClubId;
    public WowGuid128 OwnerGuid;
    public string EventName;
}

/// <summary>A raid the player is saved to, and the seconds until it resets.</summary>
public struct CalendarRaidLockout
{
    public ulong InstanceId;
    public int MapId;
    public uint DifficultyId;
    public int ExpireTime;
}

/// <summary>One event with everybody invited to it: what the event's window shows.</summary>
public sealed class CalendarSendEvent : ServerPacket
{
    public CalendarSendEvent() : base(Opcode.SMSG_CALENDAR_SEND_EVENT) { }

    public override void Write()
    {
        _worldPacket.WriteUInt8(SendType);
        _worldPacket.WritePackedGuid128(OwnerGuid);
        _worldPacket.WriteUInt64(EventId);
        _worldPacket.WriteUInt8(EventType);
        _worldPacket.WriteInt32(TextureId);
        _worldPacket.WriteUInt32(Flags);
        _worldPacket.WriteUInt32(Date);
        _worldPacket.WriteUInt32(LockDate);
        _worldPacket.WriteUInt64(EventClubId);
        _worldPacket.WriteInt32(Invites.Count);
        _worldPacket.WriteBits(System.Text.Encoding.UTF8.GetByteCount(EventName), 8);
        _worldPacket.WriteBits(System.Text.Encoding.UTF8.GetByteCount(Description), 11);
        _worldPacket.FlushBits();

        foreach (CalendarEventInvite invite in Invites)
        {
            _worldPacket.WritePackedGuid128(invite.Guid);
            _worldPacket.WriteUInt64(invite.InviteId);
            _worldPacket.WriteUInt8(invite.Level);
            _worldPacket.WriteUInt8(invite.Status);
            _worldPacket.WriteUInt8(invite.Moderator);
            _worldPacket.WriteUInt8(invite.InviteType);
            _worldPacket.WriteUInt32(invite.ResponseTime);
            _worldPacket.WriteBits(System.Text.Encoding.UTF8.GetByteCount(invite.Notes), 8);
            _worldPacket.FlushBits();
            _worldPacket.WriteString(invite.Notes);
        }

        _worldPacket.WriteString(EventName);
        _worldPacket.WriteString(Description);
    }

    public byte SendType;
    public WowGuid128 OwnerGuid = WowGuid128.Empty;
    public ulong EventId;
    public byte EventType;
    public int TextureId;
    public uint Flags;
    public uint Date;
    public uint LockDate;
    public ulong EventClubId;
    public string EventName = "";
    public string Description = "";
    public List<CalendarEventInvite> Invites = [];
}

public struct CalendarEventInvite
{
    public WowGuid128 Guid;
    public ulong InviteId;
    public byte Level;
    public byte Status;
    public byte Moderator;
    public byte InviteType;
    public uint ResponseTime;
    public string Notes;
}

/// <summary>Somebody was invited to an event, or signed up for it.</summary>
public sealed class CalendarInviteAdded : ServerPacket
{
    public CalendarInviteAdded() : base(Opcode.SMSG_CALENDAR_INVITE_ADDED) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(InviteGuid);
        _worldPacket.WriteUInt64(EventId);
        _worldPacket.WriteUInt64(InviteId);
        _worldPacket.WriteUInt8(Level);
        _worldPacket.WriteUInt8(Status);
        _worldPacket.WriteUInt8(Type);
        _worldPacket.WriteUInt32(ResponseTime);
        _worldPacket.WriteBit(ClearPending);
        _worldPacket.FlushBits();
    }

    public WowGuid128 InviteGuid = WowGuid128.Empty;
    public ulong EventId;
    public ulong InviteId;
    public byte Level;
    public byte Status;
    public byte Type;
    public uint ResponseTime;
    public bool ClearPending;
}

/// <summary>The player was invited to an event: it appears on the calendar, unanswered.</summary>
public sealed class CalendarInviteAlert : ServerPacket
{
    public CalendarInviteAlert() : base(Opcode.SMSG_CALENDAR_INVITE_ALERT) { }

    public override void Write()
    {
        _worldPacket.WriteUInt64(EventId);
        _worldPacket.WriteUInt32(Date);
        _worldPacket.WriteUInt32(Flags);
        _worldPacket.WriteUInt8(EventType);
        _worldPacket.WriteInt32(TextureId);
        _worldPacket.WriteUInt64(EventClubId);
        _worldPacket.WriteUInt64(InviteId);
        _worldPacket.WriteUInt8(Status);
        _worldPacket.WriteUInt8(ModeratorStatus);
        _worldPacket.WritePackedGuid128(InvitedByGuid);
        _worldPacket.WritePackedGuid128(OwnerGuid);
        _worldPacket.WriteBits(System.Text.Encoding.UTF8.GetByteCount(EventName), 8);
        _worldPacket.FlushBits();
        _worldPacket.WriteString(EventName);
    }

    public ulong EventId;
    public uint Date;
    public uint Flags;
    public byte EventType;
    public int TextureId;
    public ulong EventClubId;
    public ulong InviteId;
    public byte Status;
    public byte ModeratorStatus;
    public WowGuid128 InvitedByGuid = WowGuid128.Empty;
    public WowGuid128 OwnerGuid = WowGuid128.Empty;
    public string EventName = "";
}

/// <summary>An invitee answered, or a moderator answered for them.</summary>
public sealed class CalendarInviteStatus : ServerPacket
{
    public CalendarInviteStatus() : base(Opcode.SMSG_CALENDAR_INVITE_STATUS) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(InviteGuid);
        _worldPacket.WriteUInt64(EventId);
        _worldPacket.WriteUInt32(Date);
        _worldPacket.WriteUInt32(Flags);
        _worldPacket.WriteUInt8(Status);
        _worldPacket.WriteUInt32(ResponseTime);
        _worldPacket.WriteBit(ClearPending);
        _worldPacket.FlushBits();
    }

    public WowGuid128 InviteGuid = WowGuid128.Empty;
    public ulong EventId;
    public uint Date;
    public uint Flags;
    public byte Status;
    public uint ResponseTime;
    public bool ClearPending;
}

/// <summary>An invitee was taken off an event's list.</summary>
public sealed class CalendarInviteRemoved : ServerPacket
{
    public CalendarInviteRemoved() : base(Opcode.SMSG_CALENDAR_INVITE_REMOVED) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(InviteGuid);
        _worldPacket.WriteUInt64(EventId);
        _worldPacket.WriteUInt32(Flags);
        _worldPacket.WriteBit(ClearPending);
        _worldPacket.FlushBits();
    }

    public WowGuid128 InviteGuid = WowGuid128.Empty;
    public ulong EventId;
    public uint Flags;
    public bool ClearPending;
}

/// <summary>The player was taken off an event's list: the event leaves the calendar.</summary>
public sealed class CalendarInviteRemovedAlert : ServerPacket
{
    public CalendarInviteRemovedAlert() : base(Opcode.SMSG_CALENDAR_INVITE_REMOVED_ALERT) { }

    public override void Write()
    {
        _worldPacket.WriteUInt64(EventId);
        _worldPacket.WriteUInt32(Date);
        _worldPacket.WriteUInt32(Flags);
        _worldPacket.WriteUInt8(Status);
    }

    public ulong EventId;
    public uint Date;
    public uint Flags;
    public byte Status;
}

/// <summary>An invitee was made a moderator of an event, or no longer is.</summary>
public sealed class CalendarModeratorStatus : ServerPacket
{
    public CalendarModeratorStatus() : base(Opcode.SMSG_CALENDAR_MODERATOR_STATUS) { }

    public override void Write()
    {
        _worldPacket.WritePackedGuid128(InviteGuid);
        _worldPacket.WriteUInt64(EventId);
        _worldPacket.WriteUInt8(Status);
        _worldPacket.WriteBit(ClearPending);
        _worldPacket.FlushBits();
    }

    public WowGuid128 InviteGuid = WowGuid128.Empty;
    public ulong EventId;
    public byte Status;
    public bool ClearPending;
}

/// <summary>An event the player had on the calendar was deleted.</summary>
public sealed class CalendarEventRemovedAlert : ServerPacket
{
    public CalendarEventRemovedAlert() : base(Opcode.SMSG_CALENDAR_EVENT_REMOVED_ALERT) { }

    public override void Write()
    {
        _worldPacket.WriteUInt64(EventId);
        _worldPacket.WriteUInt32(Date);
        _worldPacket.WriteBit(ClearPending);
        _worldPacket.FlushBits();
    }

    public ulong EventId;
    public uint Date;
    public bool ClearPending;
}

/// <summary>An event the player had on the calendar was changed.</summary>
public sealed class CalendarEventUpdatedAlert : ServerPacket
{
    public CalendarEventUpdatedAlert() : base(Opcode.SMSG_CALENDAR_EVENT_UPDATED_ALERT) { }

    public override void Write()
    {
        _worldPacket.WriteUInt64(EventClubId);
        _worldPacket.WriteUInt64(EventId);
        _worldPacket.WriteUInt32(OriginalDate);
        _worldPacket.WriteUInt32(Date);
        _worldPacket.WriteUInt32(LockDate);
        _worldPacket.WriteUInt32(Flags);
        _worldPacket.WriteInt32(TextureId);
        _worldPacket.WriteUInt8(EventType);
        _worldPacket.WriteBits(System.Text.Encoding.UTF8.GetByteCount(EventName), 8);
        _worldPacket.WriteBits(System.Text.Encoding.UTF8.GetByteCount(Description), 11);
        _worldPacket.WriteBit(ClearPending);
        _worldPacket.FlushBits();
        _worldPacket.WriteString(EventName);
        _worldPacket.WriteString(Description);
    }

    public ulong EventClubId;
    public ulong EventId;
    public uint OriginalDate;
    public uint Date;
    public uint LockDate;
    public uint Flags;
    public int TextureId;
    public byte EventType;
    public bool ClearPending;
    public string EventName = "";
    public string Description = "";
}

/// <summary>Why a calendar request was refused; some refusals name a player.</summary>
public sealed class CalendarCommandResult : ServerPacket
{
    public CalendarCommandResult() : base(Opcode.SMSG_CALENDAR_COMMAND_RESULT) { }

    public override void Write()
    {
        _worldPacket.WriteUInt8(Command);
        _worldPacket.WriteUInt8(Result);
        _worldPacket.WriteBits(System.Text.Encoding.UTF8.GetByteCount(Name), 9);
        _worldPacket.FlushBits();
        _worldPacket.WriteString(Name);
    }

    public byte Command;
    public byte Result;
    public string Name = "";
}

/// <summary>The calendar button stops asking for the player's attention. Empty.</summary>
public sealed class CalendarClearPendingAction : ServerPacket
{
    public CalendarClearPendingAction() : base(Opcode.SMSG_CALENDAR_CLEAR_PENDING_ACTION) { }

    public override void Write() { }
}

/// <summary>The guild members a mass invitation picked, to be put on the new event's list.</summary>
public sealed class CalendarCommunityInvite : ServerPacket
{
    public CalendarCommunityInvite() : base(Opcode.SMSG_CALENDAR_COMMUNITY_INVITE) { }

    public override void Write()
    {
        _worldPacket.WriteInt32(Invites.Count);
        foreach ((WowGuid128 guid, byte level) in Invites)
        {
            _worldPacket.WritePackedGuid128(guid);
            _worldPacket.WriteUInt8(level);
        }
    }

    public List<(WowGuid128 Guid, byte Level)> Invites = [];
}

/// <summary>The player was saved to a raid: it appears on the calendar on the day it resets.</summary>
public sealed class CalendarRaidLockoutAdded : ServerPacket
{
    public CalendarRaidLockoutAdded() : base(Opcode.SMSG_CALENDAR_RAID_LOCKOUT_ADDED) { }

    public override void Write()
    {
        _worldPacket.WriteUInt64(InstanceId);
        _worldPacket.WriteUInt32(ServerTime);
        _worldPacket.WriteInt32(MapId);
        _worldPacket.WriteUInt32(DifficultyId);
        _worldPacket.WriteInt32(TimeRemaining);
    }

    public ulong InstanceId;
    public uint ServerTime;
    public int MapId;
    public uint DifficultyId;
    public int TimeRemaining;
}

/// <summary>A raid the player was saved to has reset, or the save was dropped.</summary>
public sealed class CalendarRaidLockoutRemoved : ServerPacket
{
    public CalendarRaidLockoutRemoved() : base(Opcode.SMSG_CALENDAR_RAID_LOCKOUT_REMOVED) { }

    public override void Write()
    {
        _worldPacket.WriteUInt64(InstanceId);
        _worldPacket.WriteInt32(MapId);
        _worldPacket.WriteUInt32(DifficultyId);
    }

    public ulong InstanceId;
    public int MapId;
    public uint DifficultyId;
}

/// <summary>A raid save was extended, or its extension taken back: its reset moved.</summary>
public sealed class CalendarRaidLockoutUpdated : ServerPacket
{
    public CalendarRaidLockoutUpdated() : base(Opcode.SMSG_CALENDAR_RAID_LOCKOUT_UPDATED) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32(ServerTime);
        _worldPacket.WriteInt32(MapId);
        _worldPacket.WriteUInt32(DifficultyId);
        _worldPacket.WriteInt32(OldTimeRemaining);
        _worldPacket.WriteInt32(NewTimeRemaining);
    }

    public uint ServerTime;
    public int MapId;
    public uint DifficultyId;
    public int OldTimeRemaining;
    public int NewTimeRemaining;
}
