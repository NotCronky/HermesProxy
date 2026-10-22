using System.Text;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// Talk in a dungeon finder group on a 4.3.4 server. TrinityCore 4.3.4 has no instance chat: the
/// group talks in party chat. The 4.4.2 client holds a dungeon finder group in its instance
/// category, where talk is instance chat.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class CataInstanceChatTests
{
    private static ChatPkt PartyChat(ChatMessageTypeWotLK type, GroupFlags groupFlags, byte partyIndex)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        var member = new WowGuid64(HighGuidTypeLegacy.Player, 2);
        harness.Session.GameState.CurrentGroups[partyIndex] = new PartyUpdate { PartyIndex = partyIndex, PartyFlags = groupFlags };

        const string text = "inc";
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_CHAT, p =>
        {
            p.WriteUInt8((byte)type);
            p.WriteUInt32((uint)Language.Common);
            p.WriteGuid(member);
            p.WriteUInt32(0);
            p.WriteUInt64(0);
            p.WriteUInt32((uint)text.Length + 1);
            p.WriteBytes(Encoding.UTF8.GetBytes(text + "\0"));
            p.WriteUInt8(0);
        });
        harness.Deliver(Opcode.SMSG_CHAT, wire, harness.Client.HandleServerChatMessageWotLK);
        return Assert.IsType<ChatPkt>(Assert.Single(harness.ClientWire.Sent).Packet);
    }

    [Theory]
    [InlineData(ChatMessageTypeWotLK.Party, 62)]        // CHAT_MSG_INSTANCE_CHAT
    [InlineData(ChatMessageTypeWotLK.PartyLeader, 63)]  // CHAT_MSG_INSTANCE_CHAT_LEADER
    public void PartyChat_InADungeonFinderGroup_IsInstanceChat(ChatMessageTypeWotLK type, byte modern)
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var chat = PartyChat(type, GroupFlags.Lfg | GroupFlags.LfgRestricted | GroupFlags.FakeRaid, partyIndex: 1);

        Assert.Equal(modern, (byte)chat.SlashCmd);
    }

    [Fact]
    public void PartyChat_InAParty_StaysPartyChat()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var chat = PartyChat(ChatMessageTypeWotLK.Party, GroupFlags.None, partyIndex: 0);

        Assert.Equal(ChatMessageTypeModern.Party, chat.SlashCmd);
    }
}
