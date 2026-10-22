using System;
using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A vendor that sells a currency, as TrinityCore 4.3.4 sends it, and the purchase: Talric
/// Forthright, who trades 250 Honor Points for 375 Justice Points. The entry names the server's
/// honor (392), "Honor Deprecated 3" to the 4.4.2 client, which means 1901 by honor; the client
/// buys 1901 in its own amount, which the server takes only by its own id and as a whole number of
/// the entry's amount.
/// </summary>
/// <remarks>Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public sealed class VendorCurrencyCataTests
{
    // Talric Forthright's list, nine entries; the ninth is the honor.
    private const string TalricForthright =
        "C00012D5555601000000000000004E0D0000F2CD00000100000000000000E4290100FFFFFFFF010000000200000000000000110D00004BCD" +
        "0000010000000000000034270100FFFFFFFF010000000300000000000000120D0000EFCD0000010000000000000035270100FFFFFFFF0100" +
        "00000400000000000000130D0000F1CD00000100000000000000C6290100FFFFFFFF010000000500000000000000140D0000F0CE00000100" +
        "00000000000013020100FFFFFFFF050000000600000000000000170D000012CF00000100000000000000783D0100FFFFFFFF140000000700" +
        "000000000000150D0000D9CB000001000000000000003B020100FFFFFFFF140000000800000000000000160D0000CD0C0100010000000000" +
        "0000E8760100FFFFFFFF010000000900000000000000500D000088010000020000000000000000000000FFFFFFFFA8610000CA3D87B23100" +
        "F0";

    // What the server took for the honor, once the purchase went through.
    private const string BuyHonor =
        "B38600003CCB30F1028801000009000000A86100000000000000000000FF";

    private static (LegacyHandlerHarness Harness, VendorInventory Vendor) Deliver()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true, recordServerPackets: true);
        harness.Deliver(Opcode.SMSG_VENDOR_INVENTORY, [0, 0, .. Convert.FromHexString(TalricForthright)],
            harness.Client.HandleVendorInventory);
        return (harness, harness.ClientWire.Sent.Select(s => s.Packet).OfType<VendorInventory>().Single());
    }

    [Fact]
    public void HonorOnSale_IsTheClientsHonor()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var (_, vendor) = Deliver();

        var honor = Assert.Single(vendor.Items, i => i.Type == (int)ItemVendorType.Currency);
        Assert.Equal((uint)Currency.HonorPoints, honor.Item.ItemID);
        Assert.Equal(25000u, honor.StackCount);
        Assert.Equal(3408, honor.ExtendedCostID);
        Assert.Equal(9u, honor.MuID);
    }

    [Fact]
    public void BuyingHonor_AsksForTheServersHonorInItsAmount()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var (harness, vendor) = Deliver();
        var ctx = new SessionContext(harness.Session, socket: null, harness.Client);
        // What the client sent for one purchase: its honor and its amount, 250 points in hundredths.
        var buy = new BuyItem(vendor.VendorGUID, WowGuid128.Empty, Quantity: 25000, MuID: 9, Slot: 255,
            BagSlot: 0, ItemVendorType.Currency, new ItemInstance { ItemID = (uint)Currency.HonorPoints });

        ItemSystem.HandleBuyItem(in buy, in ctx);

        var sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(Convert.FromHexString(BuyHonor), sent.Bytes);
    }
}
