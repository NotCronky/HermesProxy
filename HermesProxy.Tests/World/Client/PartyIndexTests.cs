using System;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// The packets about the group name the category the client was given the group in by its last
/// SMSG_PARTY_UPDATE: home (0), or instance (1) for a battleground's or the dungeon finder's group.
/// The client drops one that names a group it does not have: with "1 in a battleground, else 0",
/// every raid marker set in a dungeon finder group vanished.
/// </summary>
public sealed class PartyIndexTests
{
    private readonly LegacyHandlerHarness _harness = new(recordClientPackets: true);

    public PartyIndexTests()
    {
        _harness.SetActivePlayer(new WowGuid64(0x2BF));
    }

    // MSG_RAID_TARGET_UPDATE, one marker (AzerothCore Group::SetTargetIcon): skull (7) on a
    // creature, set by the player
    private SendRaidTargetUpdateSingle DeliverSkull()
    {
        byte[] wire = [0, 0, 0, .. BitConverter.GetBytes(0x2BFul), 7, .. BitConverter.GetBytes(0xF13000276E000139ul)];
        _harness.Deliver(Opcode.MSG_RAID_TARGET_UPDATE, wire, _harness.Client.HandleRaidTargetUpdate);
        return Assert.IsType<SendRaidTargetUpdateSingle>(_harness.ClientWire.Sent[^1].Packet);
    }

    [Fact]
    public void RaidTargetUpdate_InADungeonFinderGroup_NamesTheInstanceGroup()
    {
        _harness.Session.GameState.LastAnnouncedPartyIndex = 1;

        SendRaidTargetUpdateSingle update = DeliverSkull();

        Assert.Equal(1, update.PartyIndex);
        Assert.Equal(7, update.Symbol);
    }

    [Fact]
    public void RaidTargetUpdate_InAHomeGroup_NamesTheHomeGroup()
    {
        _harness.Session.GameState.LastAnnouncedPartyIndex = 0;

        Assert.Equal(0, DeliverSkull().PartyIndex);
    }
}
