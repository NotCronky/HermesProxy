using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A quest's required currencies on its progress page. A 4.3.4 server lists them after the
/// required items of SMSG_QUEST_GIVER_REQUEST_ITEMS; they were read past, so the 4.4.2 client's
/// progress page asked for nothing.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class QuestRequiredCurrencyCataTests
{
    // Quest 29507, "Fun for the Little Ones": 15 Fossil Archaeology Fragments (393), as a second
    // 4.3.4 core sent the progress page to a player holding none. TrinityCore 4.3.4 goes straight
    // to the reward page for a quest that asks for currencies only.
    private const string FunForTheLittleOnes =
        "6FA50500FF3930F14373000046756E20666F7220746865204C6974746C65204F6E657300486F772061626F75742069743F2057616E742074" +
        "6F20646F6E61746520736F6D6520666F7373696C20667261676D656E74733F00000000000000000000000000008004000000000000000000" +
        "0000000001000000890100000F0000000200000004000000080000001000000040000000";

    [Fact]
    public void ProgressPage_RequiredCurrency_IsPassedOn()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.Deliver(Opcode.SMSG_QUEST_GIVER_REQUEST_ITEMS, [0, 0, .. Convert.FromHexString(FunForTheLittleOnes)],
            harness.Client.HandleQuestGiverRequestItems);
        var page = harness.ClientWire.Sent.Select(s => s.Packet).OfType<QuestGiverRequestItems>().Single();

        Assert.Equal(29507u, page.QuestID);
        var currency = Assert.Single(page.Currency);
        Assert.Equal(393u, currency.CurrencyID);
        Assert.Equal(15, currency.Amount);
    }
}
