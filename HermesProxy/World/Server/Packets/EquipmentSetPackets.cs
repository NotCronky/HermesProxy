using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Framework.Constants;
using HermesProxy.Enums;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;

namespace HermesProxy.World.Server.Packets;

// Wrathion 3.4.3 EquipmentSetPackets.cpp. Slot count is EQUIPMENT_SET_SLOTS = 19.
// HandleUseEquipmentSet compares pieces to ObjectGuid(high=0x0C00040000000000, low=all-F).
static class EquipmentSetModern
{
    public static readonly WowGuid128 IgnoredSlot = new(0xFFFFFFFFFFFFFFFF, 0x0C00040000000000);
}

sealed class EquipmentSetID : ServerPacket
{
    public EquipmentSetID() : base(Opcode.SMSG_EQUIPMENT_SET_ID) { }

    internal static readonly ServerPacketLayouts<ServerPacketLayout<EquipmentSetID>> Layouts = new(
        (ClientVersionBuild.Zero, ClientVersionBuild.V4_4_2_60895, new GuidFirstLayout()),
        (ClientVersionBuild.V4_4_2_60895, ClientVersionBuild.Zero, new GuidLastLayout()));

    private static readonly ServerPacketLayout<EquipmentSetID> Layout = Layouts.ForRunningClient();

    public override void Write() => Layout.Write(this, _worldPacket);

    public ulong GUID;
    public int Type;
    public uint SetID;

    /// <summary>Up to 3.4.3.</summary>
    internal sealed class GuidFirstLayout : ServerPacketLayout<EquipmentSetID>
    {
        public override void Write(EquipmentSetID packet, WorldPacket data)
        {
            data.WriteUInt64(packet.GUID);
            data.WriteInt32(packet.Type);
            data.WriteUInt32(packet.SetID);
        }
    }

    /// <summary>
    /// 4.4.2 (docs/protocol, and TrinityCore cata_classic EquipmentSetID::Write): the GUID last.
    /// Written first, the client took its low half for the type and kept a GUID of 0, so a new set
    /// could not be deleted or changed.
    /// </summary>
    internal sealed class GuidLastLayout : ServerPacketLayout<EquipmentSetID>
    {
        public override void Write(EquipmentSetID packet, WorldPacket data)
        {
            data.WriteInt32(packet.Type);
            data.WriteUInt32(packet.SetID);
            data.WriteUInt64(packet.GUID);
        }
    }
}

class LoadEquipmentSet : ServerPacket
{
    public const int SlotCount = 19;

    public LoadEquipmentSet() : base(Opcode.SMSG_LOAD_EQUIPMENT_SET, ConnectionType.Instance) { }

    public override void Write()
    {
        _worldPacket.WriteUInt32((uint)Sets.Count);
        foreach (EquipmentSetData set in Sets)
            set.Write(_worldPacket);
    }

    public List<EquipmentSetData> Sets = new();
}

sealed class UseEquipmentSetResult : ServerPacket
{
    public UseEquipmentSetResult() : base(Opcode.SMSG_USE_EQUIPMENT_SET_RESULT) { }

    internal static readonly ServerPacketLayouts<ServerPacketLayout<UseEquipmentSetResult>> Layouts = new(
        (ClientVersionBuild.Zero, ClientVersionBuild.V4_4_2_60895, new GuidFirstLayout()),
        (ClientVersionBuild.V4_4_2_60895, ClientVersionBuild.Zero, new ReasonFirstLayout()));

    private static readonly ServerPacketLayout<UseEquipmentSetResult> Layout = Layouts.ForRunningClient();

    public override void Write() => Layout.Write(this, _worldPacket);

    public ulong GUID;
    public byte Reason;

    /// <summary>Up to 3.4.3.</summary>
    internal sealed class GuidFirstLayout : ServerPacketLayout<UseEquipmentSetResult>
    {
        public override void Write(UseEquipmentSetResult packet, WorldPacket data)
        {
            data.WriteUInt64(packet.GUID);
            data.WriteUInt8(packet.Reason);
        }
    }

    /// <summary>
    /// 4.4.2 (docs/protocol, and TrinityCore cata_classic UseEquipmentSetResult::Write): an int32
    /// reason, then the GUID. Written the 3.4.3 way, nine bytes with the GUID first, the client
    /// called every set it put on a failure.
    /// </summary>
    internal sealed class ReasonFirstLayout : ServerPacketLayout<UseEquipmentSetResult>
    {
        public override void Write(UseEquipmentSetResult packet, WorldPacket data)
        {
            data.WriteInt32(packet.Reason);
            data.WriteUInt64(packet.GUID);
        }
    }
}

/// <remarks>
/// Holds <see cref="EquipmentSetData"/> by reference rather than flattening it: the same type
/// carries the outbound LoadEquipmentSet payload, and outbound packets are out of scope for the
/// dispatch conversion. One allocation per saved gear set is not a hot path.
/// </remarks>
public readonly record struct SaveEquipmentSet(EquipmentSetData Set);

public readonly record struct DeleteEquipmentSet(ulong ID);

/// <summary>The nineteen equipment slots, inline so the read allocates nothing.</summary>
[InlineArray(LoadEquipmentSet.SlotCount)]
public struct EquipmentSetItems
{
    private EquipmentSetItem _element0;
}

public readonly record struct UseEquipmentSet(EquipmentSetItems Items, ulong GUID);

public struct EquipmentSetItem
{
    public WowGuid128 Item;
    public byte ContainerSlot;
    public byte Slot;
}

public class EquipmentSetData
{
    public int Type;
    public ulong Guid;
    public uint SetID;
    public uint IgnoreMask;
    public int AssignedSpecIndex = -1;
    public string SetName = "";
    public string SetIcon = "";
    public WowGuid128[] Pieces = new WowGuid128[LoadEquipmentSet.SlotCount];
    public int[] Appearances = new int[LoadEquipmentSet.SlotCount];
    public int[] Enchants = new int[2];
    public int SecondaryShoulderApparanceID;
    public int SecondaryShoulderSlot;
    public int SecondaryWeaponAppearanceID;
    public int SecondaryWeaponSlot;

    public void Write(WorldPacket data)
    {
        data.WriteInt32(Type);
        data.WriteUInt64(Guid);
        data.WriteUInt32(SetID);
        data.WriteUInt32(IgnoreMask);

        for (int i = 0; i < LoadEquipmentSet.SlotCount; i++)
        {
            data.WritePackedGuid128(Pieces[i]);
            data.WriteInt32(Appearances[i]);
        }

        data.WriteInt32(Enchants[0]);
        data.WriteInt32(Enchants[1]);
        data.WriteInt32(SecondaryShoulderApparanceID);
        data.WriteInt32(SecondaryShoulderSlot);
        data.WriteInt32(SecondaryWeaponAppearanceID);
        data.WriteInt32(SecondaryWeaponSlot);

        data.WriteBit(AssignedSpecIndex != -1);
        data.WriteBits((uint)Encoding.UTF8.GetByteCount(SetName), 8);
        data.WriteBits((uint)Encoding.UTF8.GetByteCount(SetIcon), 9);
        data.FlushBits();

        if (AssignedSpecIndex != -1)
            data.WriteInt32(AssignedSpecIndex);

        data.WriteString(SetName);
        data.WriteString(SetIcon);
    }

    public void Read(WorldPacket data)
    {
        Type = data.ReadInt32();
        Guid = data.ReadUInt64();
        SetID = data.ReadUInt32();
        IgnoreMask = data.ReadUInt32();

        for (int i = 0; i < LoadEquipmentSet.SlotCount; i++)
        {
            Pieces[i] = data.ReadPackedGuid128();
            Appearances[i] = data.ReadInt32();
        }

        Enchants[0] = data.ReadInt32();
        Enchants[1] = data.ReadInt32();
        SecondaryShoulderApparanceID = data.ReadInt32();
        SecondaryShoulderSlot = data.ReadInt32();
        SecondaryWeaponAppearanceID = data.ReadInt32();
        SecondaryWeaponSlot = data.ReadInt32();

        bool hasSpec = data.ReadBit();
        uint nameLen = data.ReadBits<uint>(8);
        uint iconLen = data.ReadBits<uint>(9);
        data.ResetBitPos();

        if (hasSpec)
            AssignedSpecIndex = data.ReadInt32();

        SetName = data.ReadString(nameLen);
        SetIcon = data.ReadString(iconLen);
    }
}
