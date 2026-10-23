using HermesProxy.Enums;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// The client takes a glyph out of a socket with CMSG_REMOVE_GLYPH. The 3.4.3.54261 client sends
/// the socket as one byte; a 3.3.5a server reads it as a uint32 (AzerothCore and CMaNGOS
/// HandleRemoveGlyph: <c>uint32 slot; recvData &gt;&gt; slot;</c>). Passed on as one byte, the read
/// ran past the end of the packet and the glyph stayed in its socket.
/// </summary>
public sealed class RemoveGlyphTests
{
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)5)]
    public void RemoveGlyph_SendsTheSocketAsUInt32(byte slot)
    {
        Assert.SkipUnless(LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056), "servers before 3.0.2 have no glyphs");
        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, null, harness.Client);

        TalentSystem.HandleRemoveGlyph(new RemoveGlyph(slot), in ctx);

        var sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(LegacyVersion.GetCurrentOpcode(Opcode.CMSG_REMOVE_GLYPH), sent.Opcode);
        Assert.Equal([slot, 0, 0, 0], sent.Bytes);
    }
}
