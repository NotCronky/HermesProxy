using System;
using Framework.Util;
using HermesProxy.Tests.Support;
using HermesProxy.World;
using HermesProxy.World.Client;
using HermesProxy.World.Enums;
using HermesProxy.World.Server.Packets;
using HermesProxy.World.Server.Systems;
using Xunit;

namespace HermesProxy.Tests.World.Client;

/// <summary>
/// The world PvP battles (Wintergrasp, Tol Barad) from a 4.3.4 core. Each server packet is written
/// the way TrinityCore 4.3.4's BattlefieldHandler.cpp writes it (a masked GUID that TrinityCore
/// leaves empty, Battlefield::m_Guid is never set) and read by the real handler; each client answer
/// is read back the way that file reads it.
/// </summary>
/// <remarks>The handler tests run only under a 4.3.4 legacy build (<c>HERMES_TEST_LEGACY_BUILD</c>).</remarks>
public class BattlefieldMgrCataTests
{
    private const ulong Battlefield = 0x1F10000000000015ul;

    [Theory]
    [InlineData(0ul)]
    [InlineData(Battlefield)]
    public void EntryInvite_TolBarad_AsksTheClientToEnterTheBattle(ulong guid)
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = NewHarness();
        uint expire = (uint)Time.UnixTime + 20;
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_BATTLEFIELD_MGR_ENTRY_INVITE, p =>
        {
            MaskedGuid.WriteMaskBits(p, guid, [5, 3, 7, 2, 6, 4, 1, 0]);
            p.FlushBits();
            MaskedGuid.WriteBytes(p, guid, [6]);
            p.WriteUInt32(BattlefieldMgrTranslation.TolBaradZoneId);
            MaskedGuid.WriteBytes(p, guid, [1, 3, 4, 2, 0]);
            p.WriteUInt32(expire);
            MaskedGuid.WriteBytes(p, guid, [7, 5]);
        });

        harness.Deliver(Opcode.SMSG_BATTLEFIELD_MGR_ENTRY_INVITE, wire, harness.Client.HandleBattlefieldMgrEntryInvite);

        var confirm = Assert.IsType<BattlefieldStatusNeedConfirmation>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(121u, confirm.Hdr.Ticket.Id);
        Assert.Equal(1116u, Assert.Single(confirm.Hdr.BattlefieldListIDs));
        Assert.Equal(85, confirm.Hdr.RangeMin);
        Assert.Equal(85, confirm.Hdr.RangeMax);
        Assert.Equal(732u, confirm.Mapid);
        Assert.InRange(confirm.Timeout, 19_000u, 20_000u);
        Assert.Equal(guid, harness.Session.GameState.BattlefieldMgrGuids[BattlefieldMgrTranslation.TolBaradBattleId]);
    }

    [Fact]
    public void EntryInvite_TakesTheBattlesQueueOffTheList()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = NewHarness();
        uint queueTicket = BattlefieldMgrTranslation.TicketFor(BattlefieldMgrTranslation.TolBaradBattleId, BattlefieldMgrTicketKind.Queue);
        harness.Session.GameState.StoreBattleFieldQueueType(queueTicket, BattlefieldMgrTranslation.ModernTolBaradListId);
        long queuedAt = harness.Session.GameState.GetBattleFieldQueueTime(queueTicket);
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_BATTLEFIELD_MGR_ENTRY_INVITE, p =>
        {
            MaskedGuid.WriteMaskBits(p, 0, [5, 3, 7, 2, 6, 4, 1, 0]);
            p.FlushBits();
            p.WriteUInt32(BattlefieldMgrTranslation.TolBaradZoneId);
            p.WriteUInt32((uint)Time.UnixTime + 20);
        });

        harness.Deliver(Opcode.SMSG_BATTLEFIELD_MGR_ENTRY_INVITE, wire, harness.Client.HandleBattlefieldMgrEntryInvite);

        Assert.Equal(2, harness.ClientWire.Sent.Count);
        var none = Assert.IsType<BattlefieldStatusNone>(harness.ClientWire.Sent[0].Packet);
        Assert.Equal(queueTicket, none.Ticket.Id);
        Assert.Equal(queuedAt, none.Ticket.Time);
        Assert.IsType<BattlefieldStatusNeedConfirmation>(harness.ClientWire.Sent[1].Packet);
        Assert.Equal(0u, harness.Session.GameState.GetBattleFieldQueueType(queueTicket));
    }

    [Fact]
    public void QueueInvite_OnTheMap_OffersThatBattlesQueue()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        // Coming from the peninsula: the invitation is ahead of the world states naming the new zone
        var harness = NewHarness();
        harness.Session.GameState.CurrentMapId = BattlefieldMgrTranslation.LegacyTolBaradMapId;
        harness.Session.GameState.CurrentZoneId = 5389;
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_BATTLEFIELD_MGR_QUEUE_INVITE, p =>
        {
            // TrinityCore 4.3.4 SendBfInvitePlayerToQueue, bit for bit
            p.WriteBit(true);
            p.WriteBit(false);
            p.WriteBit(true);
            MaskedGuid.WriteMaskBits(p, Battlefield, [0]);
            p.WriteBit(true);
            MaskedGuid.WriteMaskBits(p, Battlefield, [2, 6, 3]);
            p.WriteBit(true);
            p.WriteBit(false);
            MaskedGuid.WriteMaskBits(p, Battlefield, [1, 5, 4]);
            p.WriteBit(true);
            MaskedGuid.WriteMaskBits(p, Battlefield, [7]);
            p.FlushBits();
            MaskedGuid.WriteBytes(p, Battlefield, [2, 3, 6]);
            p.WriteUInt8(1);
            MaskedGuid.WriteBytes(p, Battlefield, [5, 0, 4, 1, 7]);
        });

        harness.Deliver(Opcode.SMSG_BATTLEFIELD_MGR_QUEUE_INVITE, wire, harness.Client.HandleBattlefieldMgrQueueInvite);

        var confirm = Assert.IsType<BattlefieldStatusNeedConfirmation>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(221u, confirm.Hdr.Ticket.Id);
        Assert.Equal(1116u, Assert.Single(confirm.Hdr.BattlefieldListIDs));
        Assert.Equal(732u, confirm.Mapid);
        Assert.Equal(Battlefield, harness.Session.GameState.BattlefieldMgrGuids[BattlefieldMgrTranslation.TolBaradBattleId]);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void QueueRequestResponse_QueuesOrRefuses(byte canQueue, bool queued)
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = NewHarness();
        byte[] wire = LegacyPacketBuilder.Build(Opcode.SMSG_BATTLEFIELD_MGR_QUEUE_REQUEST_RESPONSE, p =>
        {
            // TrinityCore 4.3.4 SendBfQueueInviteResponse: no second GUID
            MaskedGuid.WriteMaskBits(p, Battlefield, [1, 6, 5, 7]);
            p.WriteBit(false);
            MaskedGuid.WriteMaskBits(p, Battlefield, [0]);
            p.WriteBit(true);
            MaskedGuid.WriteMaskBits(p, Battlefield, [4, 3, 2]);
            p.FlushBits();
            p.WriteUInt8(canQueue);
            MaskedGuid.WriteBytes(p, Battlefield, [1, 3, 6, 7, 0]);
            p.WriteUInt8(1);
            MaskedGuid.WriteBytes(p, Battlefield, [2, 4, 5]);
            p.WriteUInt32(BattlefieldMgrTranslation.WintergraspZoneId);
        });

        harness.Deliver(Opcode.SMSG_BATTLEFIELD_MGR_QUEUE_REQUEST_RESPONSE, wire, harness.Client.HandleBattlefieldMgrQueueRequestResponse);

        var sent = Assert.Single(harness.ClientWire.Sent).Packet;
        if (queued)
        {
            var status = Assert.IsType<BattlefieldStatusQueued>(sent);
            Assert.Equal(201u, status.Hdr.Ticket.Id);
            Assert.Equal(1089u, Assert.Single(status.Hdr.BattlefieldListIDs));
            Assert.Equal(85, status.Hdr.RangeMax);
        }
        else
        {
            var failed = Assert.IsType<BattlefieldStatusFailed>(sent);
            Assert.Equal(201u, failed.Ticket.Id);
            Assert.Equal(0u, harness.Session.GameState.GetBattleFieldQueueType(201));
        }
    }

    [Fact]
    public void EnteredThenEjected_FollowTheTicketOutsideTheZone()
    {
        if (!WorldClient.IsCataLegacy)
            return;

        var harness = NewHarness();
        harness.Session.GameState.CurrentZoneId = 1519;   // accepted from Stormwind
        harness.Session.GameState.StoreBattleFieldQueueType(
            BattlefieldMgrTranslation.TicketFor(BattlefieldMgrTranslation.TolBaradBattleId, BattlefieldMgrTicketKind.Entry),
            BattlefieldMgrTranslation.ModernTolBaradListId);

        byte[] entered = LegacyPacketBuilder.Build(Opcode.SMSG_BATTLEFIELD_MGR_ENTERING, p =>
        {
            // TrinityCore 4.3.4 SendBfEntered
            p.WriteBit(false);
            p.WriteBit(true);
            MaskedGuid.WriteMaskBits(p, 0, [1, 4, 5, 0, 3]);
            p.WriteBit(false);
            MaskedGuid.WriteMaskBits(p, 0, [6, 7, 2]);
            p.FlushBits();
            MaskedGuid.WriteBytes(p, 0, [5, 3, 0, 4, 1, 7, 2, 6]);
        });
        harness.Deliver(Opcode.SMSG_BATTLEFIELD_MGR_ENTERING, entered, harness.Client.HandleBattlefieldMgrEntering);

        var active = Assert.IsType<BattlefieldStatusActive>(Assert.Single(harness.ClientWire.Sent).Packet);
        Assert.Equal(121u, active.Hdr.Ticket.Id);
        Assert.Equal(732u, active.Mapid);

        byte[] ejected = LegacyPacketBuilder.Build(Opcode.SMSG_BATTLEFIELD_MGR_EJECTED, p =>
        {
            // TrinityCore 4.3.4 SendBfLeaveMessage
            MaskedGuid.WriteMaskBits(p, 0, [2, 5, 1, 0, 3, 6]);
            p.WriteBit(false);
            MaskedGuid.WriteMaskBits(p, 0, [7, 4]);
            p.FlushBits();
            p.WriteUInt8(2);
            MaskedGuid.WriteBytes(p, 0, [1, 7, 4, 2, 3]);
            p.WriteUInt8(1);
            MaskedGuid.WriteBytes(p, 0, [6, 0, 5]);
        });
        harness.Deliver(Opcode.SMSG_BATTLEFIELD_MGR_EJECTED, ejected, harness.Client.HandleBattlefieldMgrEjected);

        Assert.Equal(3, harness.ClientWire.Sent.Count);
        var entryFailed = Assert.IsType<BattlefieldStatusFailed>(harness.ClientWire.Sent[1].Packet);
        var queueFailed = Assert.IsType<BattlefieldStatusFailed>(harness.ClientWire.Sent[2].Packet);
        Assert.Equal(121u, entryFailed.Ticket.Id);
        Assert.Equal(221u, queueFailed.Ticket.Id);
        Assert.Equal(1116u, entryFailed.BattlefieldListId);
        Assert.False(harness.Client.HasBattlefieldMgrTicket(BattlefieldMgrTranslation.TolBaradBattleId));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void InviteResponse_IsWhatTrinityCore434Reads(bool queue, bool accepted)
    {
        var kind = queue ? BattlefieldMgrTicketKind.Queue : BattlefieldMgrTicketKind.Entry;
        using var packet = new WorldPacket();
        BattlegroundSystem.WriteLegacyBfInviteResponse(packet, kind, Battlefield, accepted);

        using var r = new WorldPacket(Framed(packet.GetData()));
        var mask = new bool[8];
        bool read;
        ulong guid;
        if (kind == BattlefieldMgrTicketKind.Queue)
        {
            // HandleBfQueueInviteResponse
            ReadMask(r, mask, [2, 0, 4, 3, 5, 7]);
            read = r.HasBit();
            ReadMask(r, mask, [1, 6]);
            guid = ReadBytes(r, mask, [1, 3, 2, 4, 6, 7, 0, 5]);
        }
        else
        {
            // HandleBfEntryInviteResponse
            ReadMask(r, mask, [6, 1]);
            read = r.HasBit();
            ReadMask(r, mask, [5, 3, 2, 0, 7, 4]);
            guid = ReadBytes(r, mask, [0, 3, 4, 2, 1, 6, 7, 5]);
        }

        Assert.Equal(accepted, read);
        Assert.Equal(Battlefield, guid);
        Assert.False(r.CanRead());
    }

    [Fact]
    public void ExitRequest_IsWhatTrinityCore434Reads()
    {
        using var packet = new WorldPacket();
        BattlegroundSystem.WriteLegacyBfExitRequest(packet, Battlefield);

        using var r = new WorldPacket(Framed(packet.GetData()));
        var mask = new bool[8];
        ReadMask(r, mask, [2, 0, 3, 7, 4, 5, 6, 1]);
        Assert.Equal(Battlefield, ReadBytes(r, mask, [5, 2, 0, 1, 4, 3, 7, 6]));
        Assert.False(r.CanRead());
    }

    private static LegacyHandlerHarness NewHarness()
    {
        var harness = new LegacyHandlerHarness(recordClientPackets: true);
        harness.SetActivePlayer(new WowGuid64(HighGuidTypeLegacy.Player, 1));
        return harness;
    }

    private static byte[] Framed(byte[] payload)
    {
        byte[] framed = new byte[payload.Length + 2];
        payload.CopyTo(framed, 2);
        return framed;
    }

    private static void ReadMask(WorldPacket r, bool[] mask, int[] order)
    {
        foreach (int i in order)
            mask[i] = r.HasBit();
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
