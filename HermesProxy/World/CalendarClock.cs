using System;

namespace HermesProxy.World;

/// <summary>
/// A calendar date between the two clocks. A legacy server packs its calendar's dates in its own
/// local time; a modern client wants them in its own, as the native server sends them (TrinityCore
/// wotlk_classic CalendarMgr: the date in UTC plus the client's offset, and the client's dates
/// back the other way). The game clock is not converted: the native server sends that in its own
/// local time as well.
/// </summary>
/// <remarks>
/// The client gives only its standard offset (<c>UTCO</c> at its Battle.net logon), never daylight
/// saving: in summer a native server's dates are an hour off the player's clock. When the
/// client's standard offset is that of the zone this machine is in, which it is when the proxy
/// runs beside the client, the dates go through that zone instead, daylight saving included and
/// for each date's own season. Otherwise the offset is the client's, fixed, as on a native server.
/// </remarks>
public readonly record struct CalendarClock(TimeSpan Server, TimeSpan Client, TimeZoneInfo? ClientZone = null)
{
    /// <summary>The session's clocks, or no conversion while either side is unknown.</summary>
    public static CalendarClock For(GlobalSessionData session)
        => session.ClientUtcOffset is TimeSpan client && session.GameState?.LegacyServerUtcOffset is TimeSpan server
            ? new CalendarClock(server, client, session.ClientTimeZone)
            : default;

    /// <summary>
    /// The zone to show the client's dates in: this machine's, when the client's standard offset is
    /// that zone's.
    /// </summary>
    public static TimeZoneInfo? ZoneFor(TimeSpan clientStandardOffset, TimeZoneInfo local)
        => local.BaseUtcOffset == clientStandardOffset ? local : null;

    public uint ToClient(uint packed)
    {
        if (packed == 0 || !TryUnpack(packed, out DateTime serverLocal))
            return packed;

        DateTime utc = new DateTimeOffset(serverLocal, Server).UtcDateTime;
        DateTime clientLocal = ClientZone is null
            ? new DateTimeOffset(utc).ToOffset(Client).DateTime
            : TimeZoneInfo.ConvertTimeFromUtc(utc, ClientZone);
        return Pack(clientLocal, packed);
    }

    public uint ToServer(uint packed)
    {
        if (packed == 0 || !TryUnpack(packed, out DateTime clientLocal))
            return packed;

        DateTime utc;
        if (ClientZone is not null && !ClientZone.IsInvalidTime(clientLocal))
            utc = TimeZoneInfo.ConvertTimeToUtc(clientLocal, ClientZone);
        else
            utc = new DateTimeOffset(clientLocal, Client).UtcDateTime;
        return Pack(new DateTimeOffset(utc).ToOffset(Server).DateTime, packed);
    }

    // The same moment needs no new packing; this keeps a date the conversion leaves alone as it came.
    private static uint Pack(DateTime local, uint original)
    {
        uint packed = Time.GetPackedTimeFromDateTime(local);
        return TryUnpack(original, out DateTime before) && before == local ? original : packed;
    }

    /// <summary>
    /// The server's offset from UTC, from the local time it packs (at login, to the minute) and
    /// the time it really is: rounded to a quarter of an hour, the finest step a time zone has.
    /// </summary>
    public static TimeSpan? ServerOffset(uint packedLocalTime, DateTimeOffset utcNow)
    {
        if (!TryUnpack(packedLocalTime, out DateTime local))
            return null;

        double minutes = (local - utcNow.UtcDateTime).TotalMinutes;
        return TimeSpan.FromMinutes(Math.Round(minutes / 15) * 15);
    }

    private static bool TryUnpack(uint packed, out DateTime local)
    {
        int minute = (int)(packed & 0x3F);
        int hour = (int)(packed >> 6) & 0x1F;
        int day = (int)(packed >> 14) & 0x3F;
        int month = (int)(packed >> 20) & 0xF;
        int year = 2000 + (int)((packed >> 24) & 0x1F);
        if (minute > 59 || hour > 23 || month > 11 || day >= DateTime.DaysInMonth(year, month + 1))
        {
            local = default;
            return false;
        }

        local = new DateTime(year, month + 1, day + 1, hour, minute, 0, DateTimeKind.Unspecified);
        return true;
    }
}
