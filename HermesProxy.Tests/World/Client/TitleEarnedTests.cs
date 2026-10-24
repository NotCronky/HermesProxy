using System.Reflection;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A 3.3.5a server says a title was gained or taken away with SMSG_TITLE_EARNED: the title's bit,
/// then 1 for earned and 0 for lost (AzerothCore and cMangos Player::SetTitle). The 3.4.3 client
/// has a packet for each and says so in chat. Only a 4.3.4 server's was passed on, so on 3.3.5a
/// the title showed up in the list without a word.
/// </summary>
public sealed class TitleEarnedTests
{
    private static readonly PropertyInfo WorldPacketField =
        typeof(ServerPacket).GetProperty("_worldPacket", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static LegacyHandlerHarness Deliver(uint index, uint earned)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 77));
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_TITLE_EARNED, packet =>
        {
            packet.WriteUInt32(index);
            packet.WriteUInt32(earned);
        });
        harness.Deliver(Opcode.SMSG_TITLE_EARNED, wire, harness.Client.HandleTitleEarned);
        return harness;
    }

    [Theory]
    [InlineData(1u, Opcode.SMSG_TITLE_EARNED)]
    [InlineData(0u, Opcode.SMSG_TITLE_LOST)]
    public void TitleEarned_IsPassedOnAsEarnedOrLost(uint earned, Opcode expected)
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater && LegacyVersion.ExpansionVersion < 4,
            "a WotLK Classic client on a 3.3.5a server");

        var harness = Deliver(1, earned);

        var sent = Assert.Single(harness.ClientWire.Sent);
        Assert.Equal(expected, sent.Opcode);
        var title = Assert.IsType<TitleEarned>(sent.Packet);
        title.Write();
        Assert.Equal([1, 0, 0, 0], ((WorldPacket)WorldPacketField.GetValue(title)!).GetData());
    }

    [Fact]
    public void TitleEarned_ForAClientBeforeWotLKClassic_IsNotPassedOn()
    {
        Assert.SkipWhen(ModernVersion.IsWotLKClassicOrLater || LegacyVersion.ExpansionVersion >= 4,
            "a client before WotLK Classic on a server before 4.x");

        var harness = Deliver(1, 1);

        Assert.Empty(harness.ClientWire.Sent);
    }
}
