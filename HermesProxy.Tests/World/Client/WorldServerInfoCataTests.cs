using System.IO;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// A 4.3.4 server reports the difficulty of the instance the player enters in its own
/// SMSG_WORLD_SERVER_INFO, written as TrinityCore 4.3.4's <c>WorldServerInfo::Write</c> does: three
/// bits (restricted level, restricted money, ineligible-for-loot mask), the mask, the tournament
/// byte, money, level, the weekly reset and the difficulty. The proxy sent the client only its own
/// server info, with the map's first difficulty, so a heroic dungeon read as normal (#439).
/// </summary>
/// <remarks>
/// Runs only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>). In the collection that
/// owns <see cref="GameData"/>'s instance difficulty tables.
/// </remarks>
[Collection("V343ValuesFilter")]
public sealed class WorldServerInfoCataTests
{
    private const uint GrimBatol = 670;

    private static byte[] ServerInfo(uint difficulty, uint? lootMask = null, uint? money = null, uint? level = null)
        => LegacyPacketBuilder.Build(Opcode.SMSG_WORLD_SERVER_INFO, p =>
        {
            p.WriteBit(level.HasValue);
            p.WriteBit(money.HasValue);
            p.WriteBit(lootMask.HasValue);
            p.FlushBits();
            if (lootMask.HasValue)
                p.WriteUInt32(lootMask.Value);
            p.WriteUInt8(0);
            if (money.HasValue)
                p.WriteUInt32(money.Value);
            if (level.HasValue)
                p.WriteUInt32(level.Value);
            p.WriteUInt32(1760000000);
            p.WriteUInt32(difficulty);
        });

    private static LegacyHandlerHarness Deliver(byte[] wire)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.Session.GameState.CurrentMapId = GrimBatol;

        var (defaults, sizes) = (GameData.DefaultInstanceDifficulty, GameData.InstanceDifficultySizes);
        string csv = Path.Combine("CSV", "MapDifficulty4.csv");
        GameData.DefaultInstanceDifficulty = GameData.ParseMapDifficulties(csv);
        GameData.InstanceDifficultySizes = GameData.ParseMapDifficultySizes(csv);
        try
        {
            harness.Deliver(Opcode.SMSG_WORLD_SERVER_INFO, wire, harness.Client.HandleWorldServerInfo);
        }
        finally
        {
            (GameData.DefaultInstanceDifficulty, GameData.InstanceDifficultySizes) = (defaults, sizes);
        }
        return harness;
    }

    [Fact]
    public void HeroicDungeon_SendsTheClientItsHeroicDifficulty()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = Deliver(ServerInfo(difficulty: 1));

        var info = Assert.IsType<WorldServerInfo>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal((uint)DifficultyModern.Heroic, info.DifficultyID);
        Assert.Equal(5u, info.InstanceGroupSize);
    }

    [Fact]
    public void Optionals_AreReadPastToTheDifficulty()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = Deliver(ServerInfo(difficulty: 1, lootMask: 0x7, money: 100000, level: 20));

        var info = Assert.IsType<WorldServerInfo>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal((uint)DifficultyModern.Heroic, info.DifficultyID);
    }

    [Fact]
    public void FirstDifficulty_SendsNothing()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        // The proxy's own server info for the map already says normal.
        var harness = Deliver(ServerInfo(difficulty: 0));

        Assert.Empty(harness.ClientWire.Sent);
    }
}
