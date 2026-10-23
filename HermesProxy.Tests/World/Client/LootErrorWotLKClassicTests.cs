using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A loot a 3.3.5a server refuses: SMSG_LOOT_RESPONSE with no loot type and the reason, here a
/// second Pick Pocket on a Kobold Tunneler whose pocket the rogue had already emptied (AzerothCore
/// Player::SendLootError). The 3.4.3 client prints the reason from a response it did not acquire,
/// as TrinityCore wotlk_classic sends it; the proxy dropped the response, and the client said
/// nothing. The 1.14 and 2.5 clients are left as they were.
/// </summary>
/// <remarks>
/// The default run (a 1.14 client) proves nothing is sent; pinned to 3.4.3
/// (<c>HERMES_TEST_MODERN_BUILD=V3_4_3_54261</c>) it proves the reason reaches the client.
/// </remarks>
public sealed class LootErrorWotLKClassicTests
{
    // The Tunneler, LOOT_NONE, LOOT_ERROR_ALREADY_PICKPOCKETED, as AzerothCore sent them.
    private const string AlreadyPickpocketed = "CD1F02DB010030F1000F";

    [Fact]
    public void AlreadyPickpocketed_TellsAWotLKClassicClientWhy()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.Deliver(Opcode.SMSG_LOOT_RESPONSE, [0, 0, .. Convert.FromHexString(AlreadyPickpocketed)],
            harness.Client.HandleLootResponse);

        var sent = harness.ClientWire.Sent.Select(s => s.Packet).OfType<LootResponse>().ToList();
        if (!ModernVersion.IsWotLKClassicOrLater)
        {
            Assert.Empty(sent);
            return;
        }

        var loot = Assert.Single(sent);
        Assert.False(loot.Acquired);
        Assert.Equal(LootError.AlreadPickPocketed, loot.FailureReason);
        Assert.Equal(LootType.None, loot.AcquireReason);
        Assert.Empty(loot.Items);
    }
}
