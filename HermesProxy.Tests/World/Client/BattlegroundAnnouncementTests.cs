using System.Text;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A battleground announcement about a player, as TrinityCore 4.3.4's BuildChatPacket writes it for
/// "The Horde Flag was picked up by $n!" (BG_SYSTEM_ALLIANCE, no sender, the player as the
/// receiver; the 3.3.5a packet is the same here). The 4.4.2 client shows it only with the player
/// as the target; the older clients want the player as the sender.
/// </summary>
public class BattlegroundAnnouncementTests
{
    [Fact]
    public void FlagPickedUp_NamesThePlayerWhereTheClientLooks()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var picker = new WowGuid64(HighGuidTypeLegacy.Player, 2);
        harness.SetActivePlayer(picker);
        const string text = "The Horde Flag was picked up by $n!";
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_CHAT, p =>
        {
            p.WriteUInt8((byte)ChatMessageTypeWotLK.BattlegroundAlliance);
            p.WriteInt32(0);
            p.WriteUInt64(0);
            p.WriteUInt32(0);
            p.WriteGuid(picker);
            p.WriteUInt32((uint)text.Length + 1);
            p.WriteBytes(Encoding.UTF8.GetBytes(text + "\0"));
            p.WriteUInt8(0);
        });

        harness.Deliver(Opcode.SMSG_CHAT, wire, harness.Client.HandleServerChatMessageWotLK);

        var chat = Assert.IsType<ChatPkt>(Assert.Single(harness.ClientWire.Sent).Packet);
        WowGuid128 player = picker.To128(harness.Session.GameState);
        if (ModernVersion.BattlegroundAnnouncementNamesTarget)
        {
            Assert.Equal(player, chat.TargetGUID);
            Assert.Equal(WowGuid128.Empty, chat.SenderGUID);
        }
        else
        {
            Assert.Equal(player, chat.SenderGUID);
        }
        Assert.Equal(text, chat.ChatText);
    }
}
