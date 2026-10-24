using System;
using BNetServer;
using HermesProxy.World;
using Xunit;

namespace HermesProxy.Tests.World;

/// <summary>
/// The calendar's dates between the legacy server's local time and the client's: the client's
/// offset from its Battle.net logon (<c>UTCO</c>), the server's from its login time.
/// </summary>
public sealed class CalendarClockTests
{
    private static uint Packed(int year, int month, int day, int hour, int minute)
        => Time.GetPackedTimeFromDateTime(new DateTime(year, month, day, hour, minute, 0));

    [Theory]
    // TrinityCore wotlk_classic Timezone.cpp's table, made from the same hash
    [InlineData(0x733864AEu, 120)]
    [InlineData(0x350CA8AFu, 0)]
    [InlineData(0x15E8E23Bu, 60)]
    [InlineData(0x3F4DA929u, 330)]
    [InlineData(0x633C6B39u, -300)]
    [InlineData(0xAADC2D37u, -720)]
    public void ClientTimeZone_FromHash_IsTheOffsetInMinutes(uint hash, int minutes)
        => Assert.Equal(TimeSpan.FromMinutes(minutes), ClientTimeZone.FromHash(hash));

    [Fact]
    public void ClientTimeZone_FromDeviceId_ReadsUtco()
    {
        Assert.Equal(TimeSpan.FromHours(2), ClientTimeZone.FromDeviceId("{\"UUID\":\"x\",\"UTCO\":1933075630}"));
        Assert.Null(ClientTimeZone.FromDeviceId("{\"UUID\":\"x\"}"));
        Assert.Null(ClientTimeZone.FromDeviceId("not json"));
        Assert.Null(ClientTimeZone.FromDeviceId(""));
        Assert.Null(ClientTimeZone.FromDeviceId(null));
    }

    [Fact]
    public void ToClient_AnEventLateInTheEvening_IsTheNextDayTwoHoursOn()
    {
        var clock = new CalendarClock(Server: TimeSpan.Zero, Client: TimeSpan.FromHours(2));

        // a Friday 22:55 on a UTC server is Saturday 00:55 in UTC+2, weekday and all
        Assert.Equal(Packed(2026, 10, 17, 0, 55), clock.ToClient(Packed(2026, 10, 16, 22, 55)));
        Assert.Equal(Packed(2026, 10, 16, 22, 55), clock.ToServer(Packed(2026, 10, 17, 0, 55)));
        // across the end of a month and a year
        Assert.Equal(Packed(2027, 1, 1, 1, 30), clock.ToClient(Packed(2026, 12, 31, 23, 30)));
    }

    [Fact]
    public void ToClient_InTheMachinesZone_FollowsDaylightSavingForEachDate()
    {
        // the client says +1:00 (its standard offset); this machine's zone is Berlin's
        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var clock = new CalendarClock(Server: TimeSpan.Zero, Client: TimeSpan.FromHours(1), ClientZone: berlin);

        // in October's summer time two hours on, after the change at the end of the month one
        Assert.Equal(Packed(2026, 10, 16, 22, 55), clock.ToClient(Packed(2026, 10, 16, 20, 55)));
        Assert.Equal(Packed(2026, 10, 30, 21, 55), clock.ToClient(Packed(2026, 10, 30, 20, 55)));
        Assert.Equal(Packed(2026, 10, 16, 20, 55), clock.ToServer(Packed(2026, 10, 16, 22, 55)));
        Assert.Equal(Packed(2026, 10, 30, 20, 55), clock.ToServer(Packed(2026, 10, 30, 21, 55)));
    }

    [Fact]
    public void ZoneFor_IsTheMachinesZoneOnlyWhenItsStandardOffsetIsTheClients()
    {
        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        Assert.Same(berlin, CalendarClock.ZoneFor(TimeSpan.FromHours(1), berlin));
        Assert.Null(CalendarClock.ZoneFor(TimeSpan.FromHours(2), berlin));
    }

    [Fact]
    public void Convert_LeavesWhatIsNoDateAlone()
    {
        var clock = new CalendarClock(Server: TimeSpan.Zero, Client: TimeSpan.FromHours(2));

        Assert.Equal(0u, clock.ToClient(0));
        Assert.Equal(0xFFFFFFFFu, clock.ToClient(0xFFFFFFFF));
        Assert.Equal(Packed(2026, 10, 16, 22, 55), default(CalendarClock).ToClient(Packed(2026, 10, 16, 22, 55)));
    }

    [Theory]
    [InlineData(20, 55, 20, 54, 59, 0)]       // a UTC server, the login a second after the minute turned
    [InlineData(22, 55, 20, 54, 30, 120)]     // UTC+2
    [InlineData(2, 25, 20, 55, 0, 330)]       // UTC+5:30, the next day
    [InlineData(15, 55, 20, 55, 0, -300)]     // UTC-5
    public void ServerOffset_IsTheLocalTimeAgainstUtcToTheQuarterHour(int localHour, int localMinute, int utcHour, int utcMinute, int utcSecond, int expected)
    {
        int localDay = localHour < utcHour - 12 ? 17 : 16;
        uint packed = Packed(2026, 10, localDay, localHour, localMinute);
        var utcNow = new DateTimeOffset(2026, 10, 16, utcHour, utcMinute, utcSecond, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromMinutes(expected), CalendarClock.ServerOffset(packed, utcNow));
    }
}
