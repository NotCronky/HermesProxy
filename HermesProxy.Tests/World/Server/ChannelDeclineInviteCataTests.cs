using System.Text;
using HermesProxy.Tests.Support;
using HermesProxy.World.Client;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// A declined channel invitation reaches a 4.3.4 server as its own opcode, 0x0225, with the
/// channel's name. TrinityCore's Opcodes.h spells it <c>CSMG_CHAT_CHANNEL_DECLINE_INVITE</c>, so the
/// table generated from it had no entry and the decline was dropped with an unmapped-opcode warning.
/// </summary>
/// <remarks>
/// Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>); otherwise it returns at once.
/// </remarks>
public sealed class ChannelDeclineInviteCataTests
{
    [Fact]
    public void Decline_GoesOutAsTheOpcodeTrinityCoreSpellsCsmg()
    {
        if (!WorldClient.IsCataLegacy)
            return;
        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, socket: null, harness.Client);

        ChatSystem.HandleChatChannelDeclineInvite(new ChannelCommand("hpcata"), in ctx);

        var sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(0x0225u, sent.Opcode);
        Assert.Equal(Encoding.UTF8.GetBytes("hpcata\0"), sent.Bytes);
    }
}
