using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HermesProxy.World;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using Xunit;
using ObjectUpdateBuilder = HermesProxy.World.Objects.Version.V3_4_3_54261.ObjectUpdateBuilder;

namespace HermesProxy.Tests.World;

/// <summary>
/// 3.4.3's ActivePlayerData.SelfResSpells (bit 12), what the death popup offers instead of
/// releasing: a 3.3.5a server's PLAYER_SELF_RES_SPELL. The native server writes its mask with the
/// other dynamic masks after KnownTitles', in a bit section of their own after a flush.
/// </summary>
public sealed class SelfResSpellsWotLKTests
{
    private static readonly WowGuid128 Player = new(3, 0x0800040000000000);

    [Fact]
    public void Update_Soulstone_TheMaskThenTheSpell()
    {
        var (builder, src) = Make();
        src.SelfResSpells = [20758];
        using var data = new WorldPacket(1u);

        builder.WriteUpdateActivePlayerData(data);

        Assert.Equal(
            "01000000"              // blocks: block 0
            + "0000" + "00001001"   // 16 bits of blocks 32-47, block 0 = bits 0 and 12
            + "0000000180"          // SelfResSpells: size 1 and its changed bit, flushed
            + "16510000",           // Soulstone Resurrection, rank 2 (20758)
            Convert.ToHexString(Bytes(data)));
    }

    [Fact]
    public void Update_WithANewTitle_TheMaskAfterTheFlush()
    {
        // KnownTitles' mask is in the first section; written straight after it, the self-res
        // mask would start mid-byte, where the client does not read it.
        var (builder, src) = Make();
        src.SelfResSpells = [20758];
        src.KnownTitles[0] = 1;
        using var data = new WorldPacket(1u);

        builder.WriteUpdateActivePlayerData(data);

        Assert.Equal(
            "01000000"
            + "0000" + "00001009"       // block 0 = bits 0, 3 and 12
            + "00000006FC"              // KnownTitles: size 6 and six changed bits, flushed
            + "0000000180"              // SelfResSpells
            + "0100000000000000" + new string('0', 5 * 16)  // KnownTitles, six words
            + "16510000",
            Convert.ToHexString(Bytes(data)));
    }

    [Fact]
    public void Update_Resurrected_AnEmptyList()
    {
        // The server clears the field to 0 on resurrection; the offer must go away with it.
        var (builder, src) = Make();
        src.SelfResSpells = [0];
        using var data = new WorldPacket(1u);

        builder.WriteUpdateActivePlayerData(data);

        Assert.Equal("01000000" + "0000" + "00001001" + "00000000", Convert.ToHexString(Bytes(data)));
    }

    private static (ObjectUpdateBuilder Builder, ActivePlayerData Src) Make()
    {
        var session = (GameSessionData)RuntimeHelpers.GetUninitializedObject(typeof(GameSessionData));
        typeof(GameSessionData).GetField(nameof(GameSessionData.OriginalObjectTypes))!
            .SetValue(session, new Dictionary<WowGuid128, ObjectType>());
        session.CurrentPlayerGuid = Player;

        var globalSession = (GlobalSessionData)RuntimeHelpers.GetUninitializedObject(typeof(GlobalSessionData));
        var update = new ObjectUpdate(Player, UpdateTypeModern.Values, globalSession);
        update.ActivePlayerData ??= new ActivePlayerData();
        return (new ObjectUpdateBuilder(update, session), update.ActivePlayerData);
    }

    private static byte[] Bytes(WorldPacket data)
    {
        data.FlushBits();
        return data.GetDataSpan().ToArray();
    }
}
