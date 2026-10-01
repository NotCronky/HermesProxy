using System.Linq;
using HermesProxy.Tests.Support;
using HermesProxy.World;
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

        foreach (byte inebriation in new byte[] { 1, 49, 50, 89, 90, 100, 50, 1, 0 })
            AssertPlayerBytes(DeliverValues(harness, legacyGuid, 1, inebriation), 1, inebriation);
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
