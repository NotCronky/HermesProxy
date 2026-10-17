using System.IO;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// The saved instances of a 4.3.4 server (SMSG_RAID_INSTANCE_INFO, the raid info panel), written as
/// TrinityCore 4.3.4's Player::SendRaidInfo does: a heroic raid's lock as its size with the heroic
/// flag set, a heroic dungeon as 1 without it. The 4.4.2 client numbers both by the map's rows in
/// its own data.
/// </summary>
/// <remarks>
/// Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>). In the collection that
/// owns <see cref="GameData"/>'s instance difficulty tables.
/// </remarks>
[Collection("V343ValuesFilter")]
public sealed class RaidInstanceInfoCataTests
{
    [Fact]
    public void Locks_TakeTheClientsDifficultyForTheirMap()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 2));
        (uint Map, uint Size, uint Heroic)[] locks =
        [
            (757, 0, 0),   // Baradin Hold, 10 players
            (757, 1, 0),   // and 25
            (671, 0, 1),   // The Bastion of Twilight, 10 heroic
            (670, 1, 0),   // Grim Batol, heroic
        ];
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_RAID_INSTANCE_INFO, p =>
        {
            p.WriteInt32(locks.Length);
            ulong instance = 100;
            foreach (var (map, size, heroic) in locks)
            {
                p.WriteUInt32(map);
                p.WriteUInt32(size);
                p.WriteUInt32(heroic);
                p.WriteUInt64(instance++);
                p.WriteUInt8(1);
                p.WriteUInt8(0);
                p.WriteUInt32(3600);
                p.WriteUInt32(1);
            }
        });

        var (defaults, sizes) = (GameData.DefaultInstanceDifficulty, GameData.InstanceDifficultySizes);
        string csv = Path.Combine("CSV", "MapDifficulty4.csv");
        GameData.DefaultInstanceDifficulty = GameData.ParseMapDifficulties(csv);
        GameData.InstanceDifficultySizes = GameData.ParseMapDifficultySizes(csv);
        try
        {
            harness.Deliver(Opcode.SMSG_RAID_INSTANCE_INFO, wire, harness.Client.HandleRaidInstanceInfo);
        }
        finally
        {
            (GameData.DefaultInstanceDifficulty, GameData.InstanceDifficultySizes) = (defaults, sizes);
        }

        var info = Assert.IsType<RaidInstanceInfo>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(
            [DifficultyModern.Raid10N, DifficultyModern.Raid25N, DifficultyModern.Raid10HC, DifficultyModern.Heroic],
            info.LockList.ConvertAll(l => l.DifficultyID));
        Assert.Equal([100ul, 101ul, 102ul, 103ul], info.LockList.ConvertAll(l => l.InstanceID));
        Assert.All(info.LockList, l => Assert.Equal(1u, l.CompletedMask));
    }
}
