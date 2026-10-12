using System;
using System.Collections.Generic;
using System.Text;
using Framework.Constants;
using Framework.IO;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Dispatch;

/// <summary>
/// The paid-service race, faction and appearance changes: the modern requests, their translation
/// into the legacy opcodes, and the legacy replies back.
/// </summary>
/// <remarks>
/// The legacy requests read the appearance bytes as skin, hair color, hair style, facial hair,
/// face, while the replies (and char create) use skin, face, hair style, hair color, facial hair.
/// The two orders are what these tests pin; a swap would still produce a well-formed packet.
/// </remarks>
public class CharacterServicesTests
{
    private static readonly WowGuid64 LegacyPlayer = new(HighGuidTypeLegacy.Player, 4242);

    private static SpanPacketReader ReaderOver(byte[] payload)
    {
        byte[] framed = new byte[payload.Length + 2];
        payload.CopyTo(framed, 2);
        return new SpanPacketReader(new WorldPacket(framed).GetRemainingSpan());
    }

    private static byte[] RaceOrFactionChangeBody(WowGuid128 guid, bool factionChange, string name, byte sex,
        byte race, byte? initialRace, IReadOnlyList<(uint Option, uint Choice)> customizations)
    {
        using var w = new WorldPacket(1u);
        w.WriteBit(factionChange);
        w.WriteBits((uint)Encoding.UTF8.GetByteCount(name), 6);
        w.FlushBits();
        w.WritePackedGuid128(guid);
        w.WriteUInt8(sex);
        w.WriteUInt8(race);
        if (initialRace.HasValue)
            w.WriteUInt8(initialRace.Value);
        w.WriteInt32(customizations.Count);
        w.WriteString(name);
        foreach (var (option, choice) in customizations)
        {
            w.WriteUInt32(option);
            w.WriteUInt32(choice);
        }
        return w.GetData();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RaceOrFactionChange_ReadsWithAndWithoutInitialRace(bool withInitialRace)
    {
        var guid = new WowGuid128(0x1234UL, 0x0800000000000000UL);
        (uint, uint)[] customizations = [(3u, 17u), (1u, 5u)];
        byte[] body = RaceOrFactionChangeBody(guid, true, "Грозa", (byte)Gender.Female, (byte)Race.Human,
            withInitialRace ? (byte)Race.Orc : null, customizations);

        var r = ReaderOver(body);
        CharRaceOrFactionChangeCodec.Read(ref r, out var packet);

        Assert.True(packet.FactionChange);
        Assert.Equal(guid, packet.Guid);
        Assert.Equal("Грозa", packet.Name);
        Assert.Equal(Gender.Female, packet.Sex);
        Assert.Equal(Race.Human, packet.RaceId);
        Assert.Equal(2, packet.Customizations.Count);
        Assert.Equal(1u, packet.Customizations[0].ChrCustomizationOptionID);
        Assert.Equal(17u, packet.Customizations[1].ChrCustomizationChoiceID);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Customize_ReadsEveryField()
    {
        var guid = new WowGuid128(0x99UL, 0x0800000000000000UL);
        using var w = new WorldPacket(1u);
        w.WritePackedGuid128(guid);
        w.WriteUInt8((byte)Gender.Male);
        w.WriteInt32(1);
        w.WriteUInt32(4);
        w.WriteUInt32(8);
        w.WriteBits(5u, 6);
        w.FlushBits();
        w.WriteString("Thrak");

        var r = ReaderOver(w.GetData());
        CharCustomizeCodec.Read(ref r, out var packet);

        Assert.Equal(guid, packet.Guid);
        Assert.Equal("Thrak", packet.Name);
        Assert.Equal(Gender.Male, packet.Sex);
        Assert.Single(packet.Customizations);
        Assert.Equal(0, r.Remaining);
    }

    [Theory]
    [InlineData(true, Opcode.CMSG_CHAR_FACTION_CHANGE)]
    [InlineData(false, Opcode.CMSG_CHAR_RACE_CHANGE)]
    public void RaceOrFactionChange_GoesToTheMatchingLegacyOpcode(bool factionChange, Opcode legacyOpcode)
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true, recordServerPackets: true);
        WowGuid128 guid = LegacyPlayer.To128(harness.Session.GameState);
        var ctx = new SessionContext(harness.Session, socket: null, harness.Client);
        var change = new CharRaceOrFactionChange(guid, factionChange, "Thrak", Gender.Male, Race.Tauren, []);

        CharacterSystem.HandleCharRaceOrFactionChange(in change, in ctx);

        SentServerPacket sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(LegacyVersion.GetCurrentOpcode(legacyOpcode), sent.Opcode);
        using var body = new WorldPacket([0, 0, .. sent.Bytes]);
        Assert.Equal(LegacyPlayer, body.ReadGuid());
        Assert.Equal("Thrak", body.ReadCString());
        Assert.Equal((byte)Gender.Male, body.ReadUInt8());
        body.ReadBytes(5);
        Assert.Equal((byte)Race.Tauren, body.ReadUInt8());
        Assert.Equal(guid, harness.Session.GameState.PendingCharServiceGuid);
        Assert.Empty(harness.ClientWire.Sent);
    }

    [Fact]
    public void FactionChangeFailure_NamesTheCharacterTheRequestWasFor()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        WowGuid128 guid = LegacyPlayer.To128(harness.Session.GameState);
        harness.Session.GameState.PendingCharServiceGuid = guid;
        byte legacyInGuild = (byte)HermesProxy.World.Enums.V3_3_5a_12340.ResponseCodes.CharCreateCharacterInGuild;

        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_CHAR_FACTION_CHANGE_RESULT, p => p.WriteUInt8(legacyInGuild));
        harness.Deliver(Opcode.SMSG_CHAR_FACTION_CHANGE_RESULT, wire, harness.Client.HandleCharFactionChangeResult);

        var result = Assert.IsType<CharFactionChangeResult>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(ModernVersion.ConvertResponseCodesValue(legacyInGuild), result.Result);
        Assert.Equal(guid, result.Guid);
        Assert.Null(result.Display);
        Assert.Equal(default, harness.Session.GameState.PendingCharServiceGuid);
    }

    [Fact]
    public void FactionChangeSuccess_CarriesTheNewRaceAndName()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        WowGuid128 guid = LegacyPlayer.To128(harness.Session.GameState);
        harness.Session.GameState.PendingCharServiceGuid = guid;

        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_CHAR_FACTION_CHANGE_RESULT, p =>
        {
            p.WriteUInt8(0);
            p.WriteGuid(LegacyPlayer);
            p.WriteCString("Aldric");
            p.WriteUInt8((byte)Gender.Male);
            p.WriteUInt8(1); // skin
            p.WriteUInt8(2); // face
            p.WriteUInt8(3); // hair style
            p.WriteUInt8(4); // hair color
            p.WriteUInt8(0); // facial hair
            p.WriteUInt8((byte)Race.Human);
        });
        harness.Deliver(Opcode.SMSG_CHAR_FACTION_CHANGE_RESULT, wire, harness.Client.HandleCharFactionChangeResult);

        var result = Assert.IsType<CharFactionChangeResult>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(0, result.Result);
        Assert.Equal(guid, result.Guid);
        Assert.NotNull(result.Display);
        Assert.Equal("Aldric", result.Display.Name);
        Assert.Equal(Race.Human, result.Display.RaceId);
        Assert.Equal(Gender.Male, result.Display.Sex);
    }

    [Fact]
    public void Customize_RequestUsesTheLegacyRequestOrder_AndFailureNamesTheCharacter()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true, recordServerPackets: true);
        WowGuid128 guid = LegacyPlayer.To128(harness.Session.GameState);
        var ctx = new SessionContext(harness.Session, socket: null, harness.Client);
        var customize = new CharCustomize(guid, "Thrak", Gender.Female, []);

        CharacterSystem.HandleCharCustomize(in customize, in ctx);

        SentServerPacket sent = Assert.Single(harness.ServerWire.Sent);
        Assert.Equal(LegacyVersion.GetCurrentOpcode(Opcode.CMSG_CHAR_CUSTOMIZE), sent.Opcode);
        // guid (8) + "Thrak\0" (6) + sex + five appearance bytes
        Assert.Equal(8 + 6 + 1 + 5, sent.Bytes.Length);

        byte legacyNameInUse = (byte)HermesProxy.World.Enums.V3_3_5a_12340.ResponseCodes.CharCreateNameInUse;
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_CHAR_CUSTOMIZE, p => p.WriteUInt8(legacyNameInUse));
        harness.Deliver(Opcode.SMSG_CHAR_CUSTOMIZE, wire, harness.Client.HandleCharCustomizeResult);

        var failure = Assert.IsType<CharCustomizeFailure>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(ModernVersion.ConvertResponseCodesValue(legacyNameInUse), failure.Result);
        Assert.Equal(guid, failure.Guid);
    }
}
