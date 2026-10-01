using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using Xunit;
using ClassicBuilder = HermesProxy.World.Objects.Version.V3_4_3_54261.ObjectUpdateBuilder;

namespace HermesProxy.Tests.World.Client;

public class PlayerInebriationTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 49)]
    [InlineData(1, 49)]
    [InlineData(0, 50)]
    [InlineData(1, 50)]
    [InlineData(0, 90)]
    [InlineData(1, 90)]
    [InlineData(0, 100)]
    [InlineData(1, 100)]
    public void Values_ForActivePlayer_PreserveInebriationOnClassicWire(byte sex, byte inebriation)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var legacyGuid = new WowGuid64(HighGuidTypeLegacy.Player, 77);
        harness.SetActivePlayer(legacyGuid);

        var update = DeliverValues(harness, legacyGuid, sex, inebriation);
        AssertPlayerBytes(update, sex, inebriation);

        using var serialized = new WorldPacket();
        new ClassicBuilder(update, harness.Session.GameState).WriteUpdatePlayerData(serialized);
        using var reader = new WorldPacket(1, serialized.GetDataSpan().ToArray());

        // Native 3.4.3 PlayerData: block 0, bits 13/14/15/16, then the four byte fields.
        Assert.Equal(1u, reader.ReadBits<uint>(4));
        Assert.Equal(0x0001E001u, reader.ReadBits<uint>(32));
        Assert.True(reader.ReadBit()); // IsQuestLogChangesMaskSkipped
        Assert.Equal(sex, reader.ReadUInt8());
        Assert.Equal(inebriation, reader.ReadUInt8());
        Assert.Equal(8, reader.ReadUInt8()); // PvP title
        Assert.Equal(1, reader.ReadUInt8()); // arena faction
        Assert.False(reader.CanRead());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 50)]
    [InlineData(1, 50)]
    [InlineData(0, 100)]
    [InlineData(1, 100)]
    public void Create_PreservesInebriationOnClassicWire(byte sex, byte inebriation)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var legacyGuid = new WowGuid64(HighGuidTypeLegacy.Player, 78);
        var guid = legacyGuid.To128(harness.Session.GameState);
        var update = new ObjectUpdate(guid, UpdateTypeModern.CreateObject1, harness.Session);
        update.CreateData.ObjectType = ObjectType.Player;

        using var legacy = LegacyPacketBuilder.Receive(BuildValues(legacyGuid, sex, inebriation));
        Assert.Equal(1u, legacy.ReadUInt32());
        Assert.Equal((byte)UpdateTypeLegacy.Values, legacy.ReadUInt8());
        Assert.Equal(legacyGuid, legacy.ReadPackedGuid());
        harness.Client.ReadValuesUpdateBlockOnCreate(legacy, ref guid, ObjectType.Player, update,
            new AuraUpdate(guid, true), 0);
        AssertPlayerBytes(update, sex, inebriation);

        using var serialized = new WorldPacket();
        new ClassicBuilder(update, harness.Session.GameState).WriteCreatePlayerData(serialized);
        using var reader = new WorldPacket(1, serialized.GetDataSpan().ToArray());

        // Decode the native create prefix up to Inebriation, independent of the descriptor writer.
        for (int i = 0; i < 3; i++)
            reader.ReadPackedGuid128();
        for (int i = 0; i < 5; i++)
            reader.ReadUInt32(); // flags and guild fields
        Assert.Equal(0u, reader.ReadUInt32()); // customization count
        reader.ReadUInt8(); // PartyType
        reader.ReadUInt8(); // unknown
        reader.ReadUInt8(); // NumBankSlots
        Assert.Equal(sex, reader.ReadUInt8());
        Assert.Equal(inebriation, reader.ReadUInt8());
        Assert.Equal(8, reader.ReadUInt8());
        Assert.Equal(1, reader.ReadUInt8());
    }

    [Fact]
    public void Values_DrinkingAndSobering_ReachTheClientIncludingZero()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var legacyGuid = new WowGuid64(HighGuidTypeLegacy.Player, 77);
        harness.SetActivePlayer(legacyGuid);

        foreach (byte inebriation in (byte[])[1, 49, 50, 89, 90, 100, 50, 1, 0])
            AssertPlayerBytes(DeliverValues(harness, legacyGuid, 1, inebriation), 1, inebriation);
    }

    // Vanilla/TBC drunk values: 256 per percent from drinks, gender in bit 0. Legacy state
    // thresholds are smashed >= 23000, drunk >= 12800, tipsy for any other nonzero value.
    [Theory]
    [InlineData(0x0000, 0)]
    [InlineData(0x0001, 0)]      // gender bit alone is sober
    [InlineData(0x0002, 1)]      // legacy tipsy below one percent
    [InlineData(0x00FF, 1)]      // last tick before sobering from the 0xFFFF cap
    [InlineData(0x0100, 1)]      // one drink point
    [InlineData(0x0101, 1)]
    [InlineData(12544, 49)]      // 49 * 256, still tipsy
    [InlineData(12799, 49)]
    [InlineData(12800, 50)]      // drunk threshold
    [InlineData(12801, 50)]
    [InlineData(22784, 89)]      // 89 * 256, still drunk
    [InlineData(22999, 89)]
    [InlineData(23000, 90)]      // smashed threshold, 89.8%
    [InlineData(23039, 90)]
    [InlineData(23040, 90)]
    [InlineData(25600, 100)]     // 100 * 256
    [InlineData(32767, 100)]     // .modify drunk 50 on VMaNGOS/CMaNGOS: 0xFFFF linear scale
    [InlineData(0xFFFE, 100)]
    [InlineData(0xFFFF, 100)]
    public void LegacyDrunkValueToInebriation_MapsToModernPercentKeepingServerState(int genderAndDrunk, byte expected)
        => Assert.Equal(expected, WorldClient.LegacyDrunkValueToInebriation((ushort)genderAndDrunk));

    [Theory]
    [InlineData(0u, 0u)]       // sobered up
    [InlineData(2u, 4595u)]    // drunk from Junglevine Wine
    [InlineData(3u, 0u)]       // smashed from .modify drunk
    public void CrossedInebriationThreshold_ReachesClientOnModernWire(uint state, uint itemId)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        var legacyGuid = new WowGuid64(HighGuidTypeLegacy.Player, 77);
        var guid = harness.SetActivePlayer(legacyGuid);

        harness.Deliver(Opcode.SMSG_CROSSED_INEBRIATION_THRESHOLD,
            LegacyPacketBuilder.Build(Opcode.SMSG_CROSSED_INEBRIATION_THRESHOLD, packet =>
            {
                packet.WriteGuid(legacyGuid);
                packet.WriteUInt32(state);
                packet.WriteUInt32(itemId);
            }),
            harness.Client.HandleCrossedInebriationThreshold);

        var sent = Assert.Single(harness.ClientWire.Sent);
        Assert.Equal(Opcode.SMSG_CROSSED_INEBRIATION_THRESHOLD, sent.Opcode);
        using var reader = new WorldPacket(1, sent.Bytes);
        Assert.Equal(guid, reader.ReadPackedGuid128());
        Assert.Equal((int)state, reader.ReadInt32());
        Assert.Equal((int)itemId, reader.ReadInt32());
        Assert.False(reader.CanRead());
    }

    private static void AssertPlayerBytes(ObjectUpdate update, byte sex, byte inebriation)
    {
        Assert.NotNull(update.PlayerData);
        Assert.Equal(sex, update.PlayerData.NativeSex);
        Assert.Equal(inebriation, update.PlayerData.Inebriation);
        Assert.Equal((byte)8, update.PlayerData.PvpTitle);
        Assert.Equal((byte)1, update.PlayerData.ArenaFaction);
    }

    private static ObjectUpdate DeliverValues(LegacyHandlerHarness harness, WowGuid64 guid, byte sex,
        byte inebriation)
    {
        harness.ClientWire.Sent.Clear();
        harness.Deliver(Opcode.SMSG_UPDATE_OBJECT, BuildValues(guid, sex, inebriation),
            harness.Client.HandleUpdateObject);
        var packet = Assert.Single(harness.ClientWire.Sent.Select(s => s.Packet).OfType<UpdateObject>());
        return Assert.Single(packet.ObjectUpdates);
    }

    private static byte[] BuildValues(WowGuid64 guid, byte sex, byte inebriation)
        => LegacyPacketBuilder.Build(Opcode.SMSG_UPDATE_OBJECT, packet =>
        {
            packet.WriteUInt32(1);
            // AzerothCore/TrinityCore PLAYER_BYTES_3: sex, inebriation percent, PvP title, arena faction.
            uint playerBytes3 = sex | ((uint)inebriation << 8) | (8u << 16) | (1u << 24);
            LegacyPacketBuilder.WriteValuesBlock(packet, guid, LegacyVersion.GetUpdateField(PlayerField.PLAYER_END),
                (LegacyVersion.GetUpdateField(PlayerField.PLAYER_BYTES_3), playerBytes3));
        });
}
