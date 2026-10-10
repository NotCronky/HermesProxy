using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

public class EmoteTests
{
    /// <summary>The 3.4.3 client's empty CMSG_EMOTE reaches the server as emote 0, EMOTE_ONESHOT_NONE.</summary>
    [Fact]
    public void ClearingTheEmote_SendsEmoteZero()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, null, harness.Client);

        ChatSystem.HandleEmote(new EmptyClientPacket(), in ctx);

        var sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(LegacyVersion.GetCurrentOpcode(Opcode.CMSG_EMOTE), sent.Opcode);
        Assert.Equal(new byte[4], sent.Bytes[^4..]);
    }
}
