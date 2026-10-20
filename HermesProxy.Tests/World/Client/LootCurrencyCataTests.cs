using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World.Client;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A currency in a loot window, as TrinityCore 4.3.4 sends it: a Fossil Archaeology Find, whose
/// loot is nine Fossil Archaeology Fragments and nothing else. The currencies come after the items
/// and were read past, so the 4.4.2 client opened the find with nothing in it.
/// The client takes a currency slot as it takes an item, and TrinityCore 4.3.4 hands one out only
/// on CMSG_LOOT_CURRENCY and answers with SMSG_CURRENCY_LOOT_REMOVED.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class LootCurrencyCataTests
{
    // The find's loot: no money, no items, one currency in slot 0.
    private const string FossilFind = "21490000F42713F10A000000000001008901000009000000";

    private static (LegacyHandlerHarness Harness, LootResponse Loot) Deliver()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true, recordServerPackets: true);
        harness.Deliver(Opcode.SMSG_LOOT_RESPONSE, [0, 0, .. Convert.FromHexString(FossilFind)],
            harness.Client.HandleLootResponse);
        return (harness, harness.ClientWire.Sent.Select(s => s.Packet).OfType<LootResponse>().Single());
    }

    [Fact]
    public void Fragments_AreInTheWindow()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var (_, loot) = Deliver();

        Assert.Empty(loot.Items);
        var fragments = Assert.Single(loot.Currencies);
        Assert.Equal(393u, fragments.CurrencyID);
        Assert.Equal(9u, fragments.Quantity);
        Assert.Equal(0, fragments.LootListID);
    }

    [Fact]
    public void TakingTheFragments_AsksForTheCurrency()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var (harness, loot) = Deliver();
        var ctx = new SessionContext(harness.Session, socket: null, harness.Client);

        LootSystem.HandleLootItem(new LootItemPkt([new LootRequest(loot.LootObj, 0)]), in ctx);

        var sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(LegacyVersion.GetCurrentOpcode(Opcode.CMSG_LOOT_CURRENCY), sent.Opcode);
        Assert.Equal([0], sent.Bytes);
    }

    [Fact]
    public void FragmentsTaken_ClearTheSlotAndCloseTheWindow()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var (harness, _) = Deliver();
        harness.Deliver(Opcode.SMSG_CURRENCY_LOOT_REMOVED, [0, 0, 0], harness.Client.HandleCurrencyLootRemoved);

        var sent = harness.ClientWire.Sent.Select(s => s.Packet).Skip(1).ToList();
        Assert.Equal(2, sent.Count);
        Assert.Equal(0, Assert.IsType<LootRemoved>(sent[0]).LootListID);
        Assert.IsType<LootReleaseResponse>(sent[1]);
    }
}
