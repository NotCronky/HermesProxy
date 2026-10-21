using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A loot TrinityCore 4.3.4 refuses: SMSG_LOOT_RESPONSE with no loot type and the reason, here a
/// second Pick Pocket on a Kobold Tunneler whose pocket the rogue had already picked. The 4.4.2
/// client prints the reason from a response it did not acquire, as TrinityCore cata_classic sends
/// it; the proxy dropped the response, and the client said nothing.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class LootErrorCataTests
{
    // The Tunneler, LOOT_NONE, LOOT_ERROR_ALREADY_PICKPOCKETED.
    private const string AlreadyPickpocketed = "D9240100DB0130F1000F";

    [Fact]
    public void AlreadyPickpocketed_TellsTheClientWhy()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.Deliver(Opcode.SMSG_LOOT_RESPONSE, [0, 0, .. Convert.FromHexString(AlreadyPickpocketed)],
            harness.Client.HandleLootResponse);

        var loot = Assert.Single(harness.ClientWire.Sent.Select(s => s.Packet).OfType<LootResponse>());
        Assert.False(loot.Acquired);
        Assert.Equal(LootError.AlreadPickPocketed, loot.FailureReason);
        Assert.Equal(LootType.None, loot.AcquireReason);
        Assert.Empty(loot.Items);
    }
}
