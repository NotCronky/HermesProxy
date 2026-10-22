using System;
using System.Buffers;
using HermesProxy.Enums;
using HermesProxy.World;
using HermesProxy.World.Server.Packets;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// 4.4.2 widened SMSG_QUERY_PETITION_RESPONSE's string sizes: the title from 7 bits to 8 and each
/// choice text from 6 to 7 (TrinityCore cata_classic; the body stays 12). The old layout keeps the
/// bytes the writer sent before the split; the new one writes the widths the 4.4.2 client reads.
/// </summary>
public sealed class QueryPetitionResponseLayoutTests
{
    [Fact]
    public void Layouts_ByBuild()
    {
        Assert.IsType<QueryPetitionResponse.InfoLayout>(QueryPetitionResponse.Layouts.For(ClientVersionBuild.V2_5_3_41750));
        Assert.IsType<QueryPetitionResponse.InfoLayout>(QueryPetitionResponse.Layouts.For(ClientVersionBuild.V3_4_3_54261));
        Assert.IsType<QueryPetitionResponse.InfoLayout>(QueryPetitionResponse.Layouts.For(ClientVersionBuild.V4_4_2_60895));
        Assert.NotSame(
            QueryPetitionResponse.Layouts.For(ClientVersionBuild.V3_4_3_54261),
            QueryPetitionResponse.Layouts.For(ClientVersionBuild.V4_4_2_60895));
    }

    [Fact]
    public void OldWidths_MatchTheOldWriter()
    {
        var packet = Charter();
        AssertBothSerialisers(new QueryPetitionResponse.InfoLayout(titleBits: 7, choiceBits: 6), packet, Expected(packet, 7, 6));
    }

    [Fact]
    public void CataClassicWidths_SizeTheTitleIn8BitsAndTheChoicesIn7()
    {
        var packet = Charter();
        AssertBothSerialisers(new QueryPetitionResponse.InfoLayout(titleBits: 8, choiceBits: 7), packet, Expected(packet, 8, 7));
    }

    [Fact]
    public void Refused_WritesOnlyTheIdAndTheBit()
    {
        var packet = new QueryPetitionResponse { PetitionID = 9, Allow = false };

        using var expected = new WorldPacket(1u);
        expected.WriteUInt32(9);
        expected.WriteBit(false);
        expected.FlushBits();

        AssertBothSerialisers(new QueryPetitionResponse.InfoLayout(titleBits: 8, choiceBits: 7), packet, expected.GetDataSpan().ToArray());
    }

    private static QueryPetitionResponse Charter()
    {
        var info = new PetitionInfo
        {
            PetitionID = 1337,
            Petitioner = new WowGuid128(0x0800000000000000, 0x2C),
            Title = "Hermes Charters",
            BodyText = "",
            MinSignatures = 4,
            MaxSignatures = 4,
            AllowedClasses = 0x7FF,
            AllowedRaces = 0x6FF,
            AllowedMaxLevel = 85,
            NumChoices = 2,
        };
        info.Choicetext[0] = "Yes";
        info.Choicetext[1] = "No";
        return new QueryPetitionResponse { PetitionID = 1337, Allow = true, Info = info };
    }

    // The writer as it was before the layouts, with its widths as parameters.
    private static byte[] Expected(QueryPetitionResponse packet, int titleBits, int choiceBits)
    {
        var info = packet.Info;
        using var w = new WorldPacket(1u);
        w.WriteUInt32(packet.PetitionID);
        w.WriteBit(true);
        w.FlushBits();

        w.WriteUInt32(info.PetitionID);
        w.WritePackedGuid128(info.Petitioner);
        w.WriteUInt32(info.MinSignatures);
        w.WriteUInt32(info.MaxSignatures);
        w.WriteInt32(info.DeadLine);
        w.WriteInt32(info.IssueDate);
        w.WriteInt32(info.AllowedGuildID);
        w.WriteInt32(info.AllowedClasses);
        w.WriteInt32(info.AllowedRaces);
        w.WriteInt16(info.AllowedGender);
        w.WriteInt32(info.AllowedMinLevel);
        w.WriteInt32(info.AllowedMaxLevel);
        w.WriteInt32(info.NumChoices);
        w.WriteInt32(info.StaticType);
        w.WriteUInt32(info.Muid);

        w.WriteBits(info.Title.Length, titleBits);
        w.WriteBits(info.BodyText.Length, 12);
        foreach (string choice in info.Choicetext)
            w.WriteBits(choice.Length, choiceBits);
        w.FlushBits();

        foreach (string choice in info.Choicetext)
            w.WriteString(choice);
        w.WriteString(info.Title);
        w.WriteString(info.BodyText);
        return w.GetDataSpan().ToArray();
    }

    private static void AssertBothSerialisers(ServerPacketLayout<QueryPetitionResponse> layout, QueryPetitionResponse packet, byte[] expected)
    {
        using var data = new WorldPacket(1u);
        layout.Write(packet, data);
        Assert.Equal(expected, data.GetDataSpan().ToArray());

        byte[] buffer = ArrayPool<byte>.Shared.Rent(layout.MaxSize);
        try
        {
            int written = layout.WriteToSpan(packet, buffer);
            Assert.Equal(expected, buffer.AsSpan(0, written).ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
