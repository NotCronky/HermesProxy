using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// SMSG_LFG_BOOT_PLAYER sizes its reason in 8 bits up to 3.4.3 and in 9 from 4.4.2 (TrinityCore
/// 3.4.3 and cata_classic <c>LfgBootInfo</c>, and the 4.4.2 client's reader). The four flags come
/// first, so the width shows in the second byte: in progress, not passed, voted, voted for, and a
/// reason of five bytes.
/// </summary>
public sealed class LfgBootPlayerLayoutTests
{
    private static readonly WowGuid128 Target = new(0x0800000000000000, 0x21);

    [Fact]
    public void Layouts_ByBuild()
    {
        Assert.IsType<LfgBootPlayer.InfoLayout>(LfgBootPlayer.Layouts.For(ClientVersionBuild.V3_4_3_54261));
        Assert.IsType<LfgBootPlayer.InfoLayout>(LfgBootPlayer.Layouts.For(ClientVersionBuild.V4_4_2_60895));
        Assert.NotSame(
            LfgBootPlayer.Layouts.For(ClientVersionBuild.V3_4_3_54261),
            LfgBootPlayer.Layouts.For(ClientVersionBuild.V4_4_2_60895));
    }

    [Fact]
    public void WotLKClassic_SizesTheReasonIn8Bits()
        => AssertWrites(new LfgBootPlayer.InfoLayout(reasonBits: 8), 0xB0, 0x50);

    [Fact]
    public void CataClassic_SizesTheReasonIn9Bits()
        => AssertWrites(new LfgBootPlayer.InfoLayout(reasonBits: 9), 0xB0, 0x28);

    private static void AssertWrites(ServerPacketLayout<LfgBootPlayer> layout, byte first, byte second)
    {
        // 3.4.3's opcode table has no SMSG_LFG_BOOT_PLAYER, and a packet cannot be built without one.
        Assert.SkipWhen(Opcodes.GetOpcodeValueForVersion(Opcode.SMSG_LFG_BOOT_PLAYER, ModernVersion.Build) == 0,
            $"SMSG_LFG_BOOT_PLAYER has no opcode in {ModernVersion.Build}");

        var packet = new LfgBootPlayer
        {
            VoteInProgress = true,
            VotePassed = false,
            MyVoteCompleted = true,
            MyVote = true,
            Target = Target,
            TotalVotes = 2,
            BootVotes = 1,
            TimeLeft = 87,
            VotesNeeded = 4,
            Reason = "noisy",
        };

        using var expected = new WorldPacket(1u);
        expected.WriteUInt8(first);
        expected.WriteUInt8(second);
        expected.WritePackedGuid128(Target);
        expected.WriteUInt32(2);
        expected.WriteUInt32(1);
        expected.WriteInt32(87);
        expected.WriteUInt32(4);
        expected.WriteString("noisy");

        using var data = new WorldPacket(1u);
        layout.Write(packet, data);
        Assert.Equal(expected.GetDataSpan().ToArray(), data.GetDataSpan().ToArray());
    }
}
