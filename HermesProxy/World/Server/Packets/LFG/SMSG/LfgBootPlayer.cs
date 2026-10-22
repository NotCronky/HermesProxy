using HermesProxy.Enums;
using HermesProxy.World.Enums;
using System;

namespace HermesProxy.World.Server.Packets;

/// <summary>
/// The state of a vote to remove someone from a dungeon finder group, sent to every member when
/// the vote starts, after each vote and when it ends.
/// </summary>
public sealed class LfgBootPlayer : ServerPacket
{
    internal static readonly ServerPacketLayouts<ServerPacketLayout<LfgBootPlayer>> Layouts = new(
        (ClientVersionBuild.Zero, ClientVersionBuild.V4_4_2_60895, new InfoLayout(reasonBits: 8)),
        (ClientVersionBuild.V4_4_2_60895, ClientVersionBuild.Zero, new InfoLayout(reasonBits: 9)));

    private static readonly ServerPacketLayout<LfgBootPlayer> Layout = Layouts.ForRunningClient();

    public bool VoteInProgress;
    public bool VotePassed;
    public bool MyVoteCompleted;
    public bool MyVote;
    public WowGuid128 Target = WowGuid128.Empty;
    public uint TotalVotes;
    public uint BootVotes;
    public int TimeLeft;
    public uint VotesNeeded;
    public string Reason = "";

    public LfgBootPlayer() : base(Opcode.SMSG_LFG_BOOT_PLAYER) { }

    public override void Write() => Layout.Write(this, _worldPacket);

    /// <summary>
    /// The reason is sized in 8 bits up to 3.4.3 (TrinityCore 3.4.3 <c>LfgBootInfo</c>) and in 9
    /// from 4.4.2 (the 4.4.2 client's own reader, and TrinityCore cata_classic).
    /// </summary>
    internal sealed class InfoLayout(int reasonBits) : ServerPacketLayout<LfgBootPlayer>
    {
        public override void Write(LfgBootPlayer packet, WorldPacket data)
        {
            data.WriteBit(packet.VoteInProgress);
            data.WriteBit(packet.VotePassed);
            data.WriteBit(packet.MyVoteCompleted);
            data.WriteBit(packet.MyVote);
            data.WriteBits(packet.Reason.GetByteCount(), reasonBits);
            data.FlushBits();
            data.WritePackedGuid128(packet.Target);
            data.WriteUInt32(packet.TotalVotes);
            data.WriteUInt32(packet.BootVotes);
            data.WriteInt32(packet.TimeLeft);
            data.WriteUInt32(packet.VotesNeeded);
            data.WriteString(packet.Reason);
        }
    }
}
