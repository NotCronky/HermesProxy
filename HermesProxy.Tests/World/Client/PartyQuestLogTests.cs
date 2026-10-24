using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// The server shares every player's quest log with their group: a party member who joins sends
/// all 25 slots, their quests and zeros for the empty ones. Those must not reach the per-slot
/// QuestID cache that fills in the id the active player's own state- and progress-only updates
/// leave out. When they did, the next kill credit put the party member's quest into our slot, and
/// where their slot was empty our update went out with no id at all and was dropped.
/// </summary>
public sealed class PartyQuestLogTests
{
    private const int OwnQuest = 28774;
    private const int OwnSecondQuest = 28825;
    private const int PartyMemberQuest = 28757;

    // 3.3.5a quest log: QuestID, StateFlags, counters 0-1, counters 2-3, timer.
    private const int SlotFields = 5;

    private readonly LegacyHandlerHarness _harness = new(recordClientPackets: true);
    private readonly WowGuid64 _ownLegacy = new(HighGuidTypeLegacy.Player, 1);
    private readonly WowGuid64 _memberLegacy = new(HighGuidTypeLegacy.Player, 2);

    public PartyQuestLogTests()
    {
        _harness.SetActivePlayer(_ownLegacy);
        _harness.AddKnownObject(_memberLegacy, ObjectType.Player);

        Deliver(_ownLegacy, (Field(0, 0), OwnQuest), (Field(1, 0), OwnSecondQuest));
        // the party member's join: one quest, the rest of the log empty
        Deliver(_memberLegacy,
            (Field(0, 0), PartyMemberQuest), (Field(0, 1), 0), (Field(0, 2), 0),
            (Field(1, 0), 0), (Field(1, 1), 0), (Field(1, 2), 0));
    }

    [Fact]
    public void OwnProgressUpdate_KeepsOwnQuest_WhereThePartyMemberHasAnother()
    {
        QuestLog entry = Deliver(_ownLegacy, (Field(0, 2), 8)).PlayerData!.QuestLog[0];

        Assert.Equal(OwnQuest, entry.QuestID);
        Assert.Equal((short)8, entry.ObjectiveProgress[0]);
    }

    [Fact]
    public void OwnStateUpdate_KeepsOwnQuest_WhereThePartyMembersSlotIsEmpty()
    {
        QuestLog entry = Deliver(_ownLegacy, (Field(1, 1), 1)).PlayerData!.QuestLog[1];

        Assert.Equal(OwnSecondQuest, entry.QuestID);
        Assert.Equal(1u, entry.StateFlags);
    }

    [Fact]
    public void PartyMemberProgressUpdate_CarriesTheirOwnQuest()
    {
        QuestLog entry = Deliver(_memberLegacy, (Field(0, 2), 3)).PlayerData!.QuestLog[0];

        Assert.Equal(PartyMemberQuest, entry.QuestID);
        Assert.Equal((short)3, entry.ObjectiveProgress[0]);
        Assert.Equal(OwnQuest, _harness.Session.GameState.QuestLogQuestIDs[0]);
        Assert.Equal(OwnSecondQuest, _harness.Session.GameState.QuestLogQuestIDs[1]);
    }

    [Fact]
    public void PartyMemberUpdate_WithoutQuestFields_LeavesTheirLogOut()
    {
        var update = Deliver(_memberLegacy, (LegacyVersion.GetUpdateField(PlayerField.PLAYER_FLAGS), 0));

        Assert.True(update.PlayerData is null || update.PlayerData.QuestLog.ToArray().All(q => q is null));
    }

    private static int Field(int slot, int offset)
        => LegacyVersion.GetUpdateField(PlayerField.PLAYER_QUEST_LOG_1_1) + slot * SlotFields + offset;

    private ObjectUpdate Deliver(WowGuid64 guid, params (int Field, uint Value)[] fields)
    {
        _harness.ClientWire.Sent.Clear();
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            packet.WriteUInt32(1);
            LegacyPacketBuilder.WriteValuesBlock(packet, guid, LegacyVersion.GetUpdateField(PlayerField.PLAYER_END), fields);
        });
        _harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, wire, _harness.Client.HandleUpdateObject);
        var packet = Assert.Single(_harness.ClientWire.Sent.Select(s => s.Packet).OfType<UpdateObject>());
        return Assert.Single(packet.ObjectUpdates);
    }
}
