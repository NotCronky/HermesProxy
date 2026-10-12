using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

public sealed class GuildBankAutoStoreTests
{
    /// <summary>
    /// Taking an item out of the guild bank with a right-click. A 3.3.5a server reads the swap
    /// packet's auto-store tail as u32, u8, u32. The last was written as one byte, so the packet
    /// came up three bytes short, the server dropped it and the item stayed in the bank.
    /// </summary>
    [Fact]
    public void AutoStore_WritesTheTailAWotLKServerReads()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: false, recordServerPackets: true);
        var ctx = new SessionContext(harness.Session, null, harness.Client);
        var vault = WowGuid128.Create(HighGuidType703.GameObject, 0, 187293, 42);

        GuildSystem.HandleAutoStoreGuildBankItem(new AutoStoreGuildBankItem(vault, BankTab: 1, BankSlot: 5), in ctx);

        var sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(LegacyVersion.GetCurrentOpcode(Opcode.CMSG_GUILD_BANK_SWAP_ITEMS), sent.Opcode);
        byte[] afterTheVault =
        [
            0,              // not bank to bank
            1, 5,           // tab, slot
            0, 0, 0, 0,     // item id
            1,              // auto store
            0, 0, 0, 0,     // count
            1,              // to the character
            0, 0, 0, 0,     // always 0
        ];
        Assert.Equal(afterTheVault, sent.Bytes[^afterTheVault.Length..]);
    }
}
