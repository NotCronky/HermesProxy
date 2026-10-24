using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// The subject of a calendar mail (an event the receiver was invited to was removed): TrinityCore
/// 3.3.5a and 4.3.4 write "rawGuid:title", the modern clients read "Player-realm-counter:title"
/// (TrinityCore cata_classic BuildCalendarMailSubject, ObjectGuid::ToString) and show the title
/// with the remover as the sender.
/// </summary>
public sealed class CalendarMailSubjectTests
{
    private static WowGuid128 ToModern(WowGuid64 guid) => WowGuid128.Create(HighGuidType703.Player, guid.GetCounter());

    [Fact]
    public void LegacySubject_TheRemoverInTheClientsGuidText()
    {
        WowGuid128 remover = ToModern(new WowGuid64(2));

        string subject = CalendarMailSubject.ToClient("2:Probe invite", ToModern);

        Assert.Equal($"Player-{remover.GetRealmId()}-00000002:Probe invite", subject);
    }

    [Fact]
    public void TitleWithAColon_KeepsEverythingAfterTheFirst()
    {
        string subject = CalendarMailSubject.ToClient("26:Raid: Onyxia", ToModern);

        Assert.EndsWith("-0000001A:Raid: Onyxia", subject);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Probe invite")]
    [InlineData("0:Probe invite")]
    [InlineData("GUID Full: 0x0000000000000002 Type: Player Low: 2:Probe invite")]
    public void NotARawGuid_LeftAsItIs(string legacy)
    {
        Assert.Equal(legacy, CalendarMailSubject.ToClient(legacy, ToModern));
    }

    [Fact]
    public void CalendarIsTheFifthType()
    {
        // TrinityCore MAIL_CALENDAR; the modern writers put an int sender id after it, as for an
        // auction, a creature or a game object.
        Assert.Equal(5, (int)MailType.Calendar);
    }
}
