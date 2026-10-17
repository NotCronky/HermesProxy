using System;
using System.Collections.Generic;
using Framework.IO;
using HermesProxy.World;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Server;

/// <summary>
/// Transmogrification between the 4.4.2 client and a 4.3.4 server (#411). The client asks by
/// appearance (its layout: count, transmogrifier, per slot appearance, slot, illusion, second
/// appearance, then a bit). A 4.3.4 server reads TrinityCore 4.3.4 TransmogrifyItems::Read: the count
/// in 22 bits, each source item's GUID mask, the transmogrifier's mask, then per slot the source
/// entry, the source GUID's bytes and the slot, then the transmogrifier's bytes.
/// </summary>
public class TransmogrifyTests
{
    private static readonly WowGuid128 Npc = new(0x0000001200000000ul, 0x203ECC1080000000ul);

    private static SpanPacketReader ReaderOver(byte[] payload)
    {
        byte[] framed = new byte[payload.Length + 2];
        payload.CopyTo(framed, 2);
        return new SpanPacketReader(new WorldPacket(framed).GetRemainingSpan());
    }

    [Fact]
    public void Codec_ReadsEverySlotAndTheBit()
    {
        using var w = new WorldPacket(1u);
        w.WriteUInt32(2);
        w.WritePackedGuid128(Npc);
        w.WriteInt32(118743); w.WriteUInt32(15); w.WriteInt32(0); w.WriteInt32(0);
        w.WriteInt32(0); w.WriteUInt32(2); w.WriteInt32(0); w.WriteInt32(0);
        w.WriteBit(true);
        w.FlushBits();

        var r = ReaderOver(w.GetData());
        TransmogrifyItemsCodec.Read(ref r, out var request);

        Assert.Equal(Npc, request.Npc);
        Assert.Equal(2, request.Items.Length);
        Assert.Equal(new TransmogrifyItem(118743, 15, 0, 0), request.Items[0]);
        Assert.Equal(new TransmogrifyItem(0, 2, 0, 0), request.Items[1]);
        Assert.True(request.CurrentSpecOnly);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void LegacyWriter_IsWhatTrinityCore434Reads()
    {
        const ulong npc = 0xF130D49A0000A3C1ul;
        var slots = new List<(ulong Source, uint Entry, uint Slot)>
        {
            (0x4000000000002A11ul, 3417, 15),   // the main hand takes the Onyx Claymore's look
            (0, 0, 4),                          // the chest takes its own back
        };

        using var packet = new WorldPacket();
        ItemSystem.WriteLegacyTransmogrify(packet, npc, slots);

        // Read back the way TrinityCore 4.3.4 does.
        using var r = new WorldPacket(Framed(packet.GetData()));
        uint count = r.ReadBits<uint>(22);
        Assert.Equal(2u, count);
        var sourceMasks = new bool[count][];
        for (int i = 0; i < count; i++)
            sourceMasks[i] = ReadMask(r, [0, 5, 6, 2, 3, 7, 4, 1]);
        bool[] npcMask = ReadMask(r, [7, 3, 5, 6, 1, 4, 0, 2]);
        for (int i = 0; i < count; i++)
        {
            uint entry = r.ReadUInt32();
            ulong source = ReadBytes(r, sourceMasks[i], [1, 5, 0, 4, 6, 7, 3, 2]);
            uint slot = r.ReadUInt32();
            Assert.Equal(slots[i], (source, entry, slot));
        }
        Assert.Equal(npc, ReadBytes(r, npcMask, [7, 2, 5, 4, 3, 1, 6, 0]));
        Assert.False(r.CanRead());
    }

    private static byte[] Framed(byte[] payload)
    {
        byte[] framed = new byte[payload.Length + 2];
        payload.CopyTo(framed, 2);
        return framed;
    }

    private static bool[] ReadMask(WorldPacket r, int[] order)
    {
        var mask = new bool[8];
        foreach (int i in order)
            mask[i] = r.HasBit();
        return mask;
    }

    private static ulong ReadBytes(WorldPacket r, bool[] mask, int[] order)
    {
        Span<byte> bytes = stackalloc byte[8];
        foreach (int i in order)
        {
            if (mask[i])
                bytes[i] = (byte)(r.ReadUInt8() ^ 1);
        }
        return BitConverter.ToUInt64(bytes);
    }
}
