using System.Runtime.CompilerServices;
using Framework.IO;
using HermesProxy.World.Dispatch;

namespace HermesProxy.World.Server.Packets;

// Calendar CMSG codecs: the 3.4.3 client's layouts (native 3.4.3 server CalendarPackets.cpp). A
// date is a packed date and stays the number it came as. The club id that most of them carry (the
// guild or community an event belongs to) is read past: a legacy server knows a guild event by
// the event's own flags.

public static class CalendarGetEventCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref SpanPacketReader r, out CalendarGetEvent packet)
        => packet = new CalendarGetEvent(r.ReadUInt64());
}

public static class CalendarAddEventCodec
{
    // the smallest invitation: an empty GUID's two mask bytes, two numbers, three bits
    private const int MinInviteBytes = 5;

    public static void Read(ref SpanPacketReader r, out CalendarAddEvent packet)
    {
        r.ReadUInt64();     // club id
        byte eventType = r.ReadUInt8();
        int textureId = r.ReadInt32();
        uint time = r.ReadUInt32();
        uint flags = r.ReadUInt32();
        int inviteCount = r.ReadInt32();
        uint titleLength = r.ReadBits<uint>(8);
        uint descriptionLength = r.ReadBits<uint>(11);

        // The count is wire data: what is left of the packet bounds how many can follow.
        if (inviteCount < 0 || inviteCount > r.Remaining / MinInviteBytes)
            throw new System.ArgumentOutOfRangeException(nameof(inviteCount), inviteCount,
                $"CMSG_CALENDAR_ADD_EVENT claims {inviteCount} invitations but only {r.Remaining} bytes remain.");

        CalendarAddEventInvite[] invites = new CalendarAddEventInvite[inviteCount];
        for (int i = 0; i < inviteCount; i++)
        {
            WowGuid128 guid = r.ReadPackedGuid128();
            byte status = r.ReadUInt8();
            byte moderator = r.ReadUInt8();
            // three optional values a legacy server has no place for
            bool hasGuid = r.HasBit();
            bool hasFirstNumber = r.HasBit();
            bool hasSecondNumber = r.HasBit();
            if (hasGuid)
                r.ReadPackedGuid128();
            if (hasFirstNumber)
                r.ReadUInt64();
            if (hasSecondNumber)
                r.ReadUInt64();
            invites[i] = new CalendarAddEventInvite(guid, status, moderator);
        }

        string title = r.ReadString(titleLength);
        string description = r.ReadString(descriptionLength);
        r.ReadUInt32();     // the most invitations the client allows
        packet = new CalendarAddEvent(eventType, textureId, time, flags, invites, title, description);
    }
}

public static class CalendarUpdateEventCodec
{
    public static void Read(ref SpanPacketReader r, out CalendarUpdateEvent packet)
    {
        r.ReadUInt64();     // club id
        ulong eventId = r.ReadUInt64();
        ulong moderatorId = r.ReadUInt64();
        byte eventType = r.ReadUInt8();
        int textureId = r.ReadInt32();
        uint time = r.ReadUInt32();
        uint flags = r.ReadUInt32();
        uint titleLength = r.ReadBits<uint>(8);
        uint descriptionLength = r.ReadBits<uint>(11);
        string title = r.ReadString(titleLength);
        string description = r.ReadString(descriptionLength);
        r.ReadUInt32();     // the most invitations the client allows
        packet = new CalendarUpdateEvent(eventId, moderatorId, eventType, textureId, time, flags, title, description);
    }
}

public static class CalendarRemoveEventCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref SpanPacketReader r, out CalendarRemoveEvent packet)
    {
        ulong eventId = r.ReadUInt64();
        ulong moderatorId = r.ReadUInt64();
        r.ReadUInt64();     // club id
        uint flags = r.ReadUInt32();
        packet = new CalendarRemoveEvent(eventId, moderatorId, flags);
    }
}

public static class CalendarCopyEventCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref SpanPacketReader r, out CalendarCopyEvent packet)
    {
        ulong eventId = r.ReadUInt64();
        ulong moderatorId = r.ReadUInt64();
        r.ReadUInt64();     // club id
        uint date = r.ReadUInt32();
        packet = new CalendarCopyEvent(eventId, moderatorId, date);
    }
}

public static class CalendarInviteCodec
{
    public static void Read(ref SpanPacketReader r, out CalendarInvite packet)
    {
        ulong eventId = r.ReadUInt64();
        ulong moderatorId = r.ReadUInt64();
        r.ReadUInt64();     // club id
        uint nameLength = r.ReadBits<uint>(9);
        bool creating = r.HasBit();
        bool isSignUp = r.HasBit();
        string name = r.ReadString(nameLength);
        packet = new CalendarInvite(eventId, moderatorId, name, creating, isSignUp);
    }
}

public static class CalendarEventSignUpCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref SpanPacketReader r, out CalendarEventSignUp packet)
    {
        ulong eventId = r.ReadUInt64();
        r.ReadUInt64();     // club id
        bool tentative = r.HasBit();
        packet = new CalendarEventSignUp(eventId, tentative);
    }
}

public static class CalendarRsvpCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref SpanPacketReader r, out CalendarRsvp packet)
    {
        ulong eventId = r.ReadUInt64();
        ulong inviteId = r.ReadUInt64();
        byte status = r.ReadUInt8();
        packet = new CalendarRsvp(eventId, inviteId, status);
    }
}

public static class CalendarRemoveInviteCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref SpanPacketReader r, out CalendarRemoveInvite packet)
    {
        WowGuid128 guid = r.ReadPackedGuid128();
        ulong inviteId = r.ReadUInt64();
        ulong moderatorId = r.ReadUInt64();
        ulong eventId = r.ReadUInt64();
        packet = new CalendarRemoveInvite(guid, inviteId, moderatorId, eventId);
    }
}

// CMSG_CALENDAR_STATUS and CMSG_CALENDAR_MODERATOR_STATUS
public static class CalendarInviteeStatusCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref SpanPacketReader r, out CalendarInviteeStatus packet)
    {
        WowGuid128 guid = r.ReadPackedGuid128();
        ulong eventId = r.ReadUInt64();
        ulong inviteId = r.ReadUInt64();
        ulong moderatorId = r.ReadUInt64();
        byte status = r.ReadUInt8();
        packet = new CalendarInviteeStatus(guid, eventId, inviteId, moderatorId, status);
    }
}

public static class CalendarComplainCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref SpanPacketReader r, out CalendarComplain packet)
    {
        WowGuid128 invitedBy = r.ReadPackedGuid128();
        ulong eventId = r.ReadUInt64();
        ulong inviteId = r.ReadUInt64();
        packet = new CalendarComplain(invitedBy, eventId, inviteId);
    }
}

public static class CalendarCommunityInviteRequestCodec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Read(ref SpanPacketReader r, out CalendarCommunityInviteRequest packet)
    {
        ulong clubId = r.ReadUInt64();
        byte minLevel = r.ReadUInt8();
        byte maxLevel = r.ReadUInt8();
        byte maxRankOrder = r.ReadUInt8();
        packet = new CalendarCommunityInviteRequest(clubId, minLevel, maxLevel, maxRankOrder);
    }
}
