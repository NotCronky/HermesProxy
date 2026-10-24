using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A 3.3.5a server tells of a death knight's rune changing kind with SMSG_CONVERT_RUNE: the rune's
/// slot, then its new kind in the server's numbers (Blood 0, Unholy 1, Frost 2, Death 3). The 3.4.3
/// client has no other word of a death rune; it numbers the kinds Blood 0, Frost 1, Unholy 2,
/// Death 3 (RuneFrame.lua). The packet was kept from it, so Blood Tap never showed a death rune.
/// </summary>
public sealed class DkConvertRuneTests
{
    [Theory]
    [InlineData(0, 3, 3u)] // Blood Tap: a Blood rune turns Death
    [InlineData(0, 0, 0u)] // and back
    [InlineData(2, 1, 2u)] // an Unholy rune back from Death
    [InlineData(4, 2, 1u)] // a Frost rune back from Death
    public void ConvertRune_GoesOnInTheClientsRuneTypes(byte index, byte legacyRune, uint rune)
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater && LegacyVersion.ExpansionVersion < 4,
            "a WotLK Classic client on a 3.3.5a server");
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 5));
        harness.Session.GameState.RuneState = new RuneStateData();

        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_CONVERT_RUNE, packet =>
        {
            packet.WriteUInt8(index);
            packet.WriteUInt8(legacyRune);
        });
        harness.Deliver(Opcode.SMSG_CONVERT_RUNE, wire, harness.Client.HandleConvertRune);

        var sent = Assert.Single(harness.ClientWire.Sent);
        Assert.Equal(Opcode.SMSG_CONVERT_RUNE, sent.Opcode);
        // an empty rune list, then the index and the type
        Assert.Equal([0, 0, 0, 0, 0, 0, index, 0, 0, 0, (byte)rune, 0, 0, 0], sent.Bytes);
        Assert.Equal(legacyRune, harness.Session.GameState.RuneState!.RuneTypes[index]);
    }
}
