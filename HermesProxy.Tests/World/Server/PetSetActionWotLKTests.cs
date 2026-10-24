using System;
using Framework.IO;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// A command or stance dropped on another slot of the pet bar: the 3.4.3 client sends one
/// CMSG_PET_SET_ACTION with both slots, the second after a flag bit. A 3.3.5a server moves a
/// command only as one packet with both slots (<c>HandlePetSetAction</c>, count 2) and drops it on
/// its own, so with the second slot left out a moved Attack stayed where it was on the server's
/// bar. From an AzerothCore capture of a hunter dropping Attack (slot 1) on Growl (slot 4).
/// </summary>
/// <remarks>Runs with a 3.4.3 client (<c>HERMES_TEST_MODERN_BUILD</c>) and a 3.3.5a server.</remarks>
public sealed class PetSetActionWotLKTests
{
    private const string AttackOntoGrowl = "03a74e0180fe01042803000000020080038000000000590a80c0";

    [Fact]
    public void CommandDroppedOnASpell_GoesOutAsOnePacketWithBothSlots()
    {
        Assert.SkipUnless(ModernVersion.IsWotLKClassicOrLater && LegacyVersion.ExpansionVersion == 3, "a 3.4.3 client on a 3.3.5a server");

        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, socket: null, harness.Client);
        var r = new SpanPacketReader(Convert.FromHexString(AttackOntoGrowl));
        PetSetActionCodec.Read(ref r, out var action);

        PetSystem.HandlePetSetAction(in action, in ctx);

        var sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(Opcode.CMSG_PET_SET_ACTION, LegacyVersion.GetUniversalOpcode(sent.Opcode));
        ulong pet = action.PetGUID.To64(harness.Session.GameState).GetLowValue();
        Assert.Equal(pet, BitConverter.ToUInt64(sent.Bytes, 0));
        // Attack (ACT_COMMAND 2) at slot 3, then Growl (ACT_ENABLED 2649) at slot 0.
        Assert.Equal("03000000" + "02000007" + "00000000" + "590A00C1", Convert.ToHexString(sent.Bytes, 8, sent.Bytes.Length - 8));
    }
}
