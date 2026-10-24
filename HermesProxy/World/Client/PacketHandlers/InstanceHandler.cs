using HermesProxy.Enums;
using HermesProxy.World.Dispatch;
using HermesProxy.World.Enums;
using HermesProxy.World.Objects;
using HermesProxy.World.Server.Packets;
using System;
using System.Collections.Generic;

namespace HermesProxy.World.Client;

public partial class WorldClient
{
    // Handlers for SMSG opcodes coming the legacy world server

    /// <summary>
    /// The "you will be saved to this instance" confirmation. Legacy sends two uint32s and a byte
    /// (Map.cpp, perm-bound branch); V3_4_3 renamed the opcode to SMSG_PENDING_RAID_LOCK and packs
    /// the trailing flags as bits. Without this the warning never reached the client, so the
    /// client never sent CMSG_INSTANCE_LOCK_RESPONSE and the server sat on a pending bind.
    /// </summary>
    [HandlesSmsg(Opcode.SMSG_INSTANCE_LOCK_WARNING_QUERY)]
    internal void HandleInstanceLockWarningQuery(WorldPacket packet)
    {
        PendingRaidLock pending = new PendingRaidLock();
        pending.TimeUntilLock = (int)packet.ReadUInt32();
        pending.CompletedMask = packet.ReadUInt32();
        pending.Extending = packet.ReadUInt8() != 0;
        // 3.3.5a has no equivalent, it only asks when a bind is pending; 4.3.4 PendingRaidLock says.
        pending.WarningOnly = IsCataLegacy && packet.ReadBool();
        SendPacketToClient(pending);
    }

    /// <summary>
    /// The difficulty the legacy server put the player's instance in, sent on login and on every
    /// map change after the new-world packet. The world server info already sent for the map
    /// carried its first difficulty, so a heroic or 25-player instance showed as normal or
    /// 10-player; a second one corrects it, as a native 3.4.3 server reports the instance's own
    /// difficulty at this point.
    /// </summary>
    [HandlesSmsg(Opcode.SMSG_INSTANCE_DIFFICULTY)]
    internal void HandleInstanceDifficulty(WorldPacket packet)
    {
        SendInstanceDifficulty(packet.ReadUInt32());
    }

    /// <summary>
    /// A 4.3.4 server has no SMSG_INSTANCE_DIFFICULTY: it reports the instance's difficulty in a
    /// server info of its own, at the same point (TrinityCore 4.3.4
    /// <c>Player::SendInitialPacketsBeforeAddToMap</c>). Only the difficulty is taken from it; the
    /// rest of the client's server info stays the proxy's.
    /// </summary>
    [HandlesSmsg(Opcode.SMSG_WORLD_SERVER_INFO)]
    internal void HandleWorldServerInfo(WorldPacket packet)
    {
        bool hasRestrictedLevel = packet.HasBit();
        bool hasRestrictedMoney = packet.HasBit();
        bool hasIneligibleForLootMask = packet.HasBit();
        if (hasIneligibleForLootMask)
            packet.ReadUInt32();
        packet.ReadUInt8(); // IsTournamentRealm
        if (hasRestrictedMoney)
            packet.ReadUInt32();
        if (hasRestrictedLevel)
            packet.ReadUInt32();
        packet.ReadUInt32(); // WeeklyReset
        SendInstanceDifficulty(packet.ReadUInt32());
    }

    private void SendInstanceDifficulty(uint legacyDifficulty)
    {
        uint? mapId = GetSession().GameState.CurrentMapId;
        if (legacyDifficulty == 0 || mapId is null || !WorldServerInfo.FollowsInstanceDifficulty)
            return;

        SendPacketToClient(WorldServerInfo.ForMap(mapId.Value, legacyDifficulty));
    }

    [HandlesSmsg(Opcode.SMSG_UPDATE_INSTANCE_OWNERSHIP)]
    internal void HandleUpdateInstanceOwnership(WorldPacket packet)
    {
        UpdateInstanceOwnership instance = new UpdateInstanceOwnership();
        instance.IOwnInstance = packet.ReadUInt32();
        SendPacketToClient(instance);
    }

    [HandlesSmsg(Opcode.SMSG_UPDATE_LAST_INSTANCE)]
    internal void HandleUpdateLastInstance(WorldPacket packet)
    {
        UpdateLastInstance last = new();
        last.MapID = packet.ReadUInt32();
        SendPacketToClient(last);
    }

    [HandlesSmsg(Opcode.SMSG_INSTANCE_RESET)]
    internal void HandleInstanceReset(WorldPacket packet)
    {
        InstanceReset reset = new InstanceReset();
        reset.MapID = packet.ReadUInt32();
        SendPacketToClient(reset);
    }

    [HandlesSmsg(Opcode.SMSG_INSTANCE_RESET_FAILED)]
    internal void HandleInstanceResetFailed(WorldPacket packet)
    {
        InstanceResetFailed reset = new InstanceResetFailed();
        reset.ResetFailedReason = (ResetFailedReason)packet.ReadUInt32();
        reset.MapID = packet.ReadUInt32();
        SendPacketToClient(reset);
    }

    [HandlesSmsg(Opcode.SMSG_RESET_FAILED_NOTIFY)]
    internal void HandleResetFailedNotify(WorldPacket packet)
    {
        ResetFailedNotify reset = new ResetFailedNotify();
        packet.ReadUInt32(); // Map ID
        SendPacketToClient(reset);
    }

    [HandlesSmsg(Opcode.SMSG_RAID_INSTANCE_INFO)]
    internal void HandleRaidInstanceInfo(WorldPacket packet)
    {
        RaidInstanceInfo infos = new RaidInstanceInfo();
        int count = packet.ReadInt32();
        for (var i = 0; i < count; ++i)
        {
            InstanceLock instance = new InstanceLock();
            instance.MapID = packet.ReadUInt32();

            if (IsCataLegacy)
            {
                // TrinityCore 4.3.4 Player::SendRaidInfo: a heroic raid's lock as its size (0 for
                // 10, 1 for 25) with the heroic flag after it; a heroic dungeon is 1 without the
                // flag. Turned into one number of 3.3.5a's, the map's rows in the client's data
                // tell a raid from a dungeon. Passed on as it came, a 10-player raid was "no
                // difficulty" and a 25-player one "normal".
                uint size = packet.ReadUInt32();
                bool heroic = packet.ReadUInt32() != 0;
                instance.DifficultyID = (DifficultyModern)GameData.ModernInstanceDifficulty(instance.MapID, heroic ? size + 2 : size).DifficultyId;
            }
            else if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
                instance.DifficultyID = (DifficultyModern)packet.ReadUInt32();
            else
            {
                if (ModernVersion.ExpansionVersion == 1)
                    instance.DifficultyID = DifficultyModern.Raid40;
                else
                    instance.DifficultyID = DifficultyModern.Raid25N;
            }

            if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            {
                // TrinityCore 4.3.4 InstanceLock: the bosses killed after the time. Without them
                // (and the heroic flag) every lock after the first is read from the middle of the
                // one before.
                instance.InstanceID = packet.ReadUInt64();
                instance.Locked = packet.ReadBool();
                instance.Extended = packet.ReadBool();
                instance.TimeRemaining = packet.ReadInt32();
                if (IsCataLegacy)
                    instance.CompletedMask = packet.ReadUInt32();
            }
            else
            {
                instance.TimeRemaining = packet.ReadInt32();
                instance.InstanceID = packet.ReadUInt32();

                if (LegacyVersion.AddedInVersion(ClientVersionBuild.V2_0_1_6180))
                    packet.ReadUInt32(); // Counter
            }
            infos.LockList.Add(instance);
        }
        SendPacketToClient(infos);
    }

    [HandlesSmsg(Opcode.SMSG_INSTANCE_SAVE_CREATED)]
    internal void HandleInstanceSaveCreated(WorldPacket packet)
    {
        InstanceSaveCreated save = new InstanceSaveCreated();
        save.Gm = packet.ReadUInt32() != 0;
        SendPacketToClient(save);
    }

    [HandlesSmsg(Opcode.SMSG_RAID_GROUP_ONLY)]
    internal void HandleRaidGroupOnly(WorldPacket packet)
    {
        RaidGroupOnly save = new RaidGroupOnly();
        save.Delay = packet.ReadInt32();
        save.Reason = (RaidGroupReason)packet.ReadUInt32();
        SendPacketToClient(save);
    }

    [HandlesSmsg(Opcode.SMSG_RAID_INSTANCE_MESSAGE)]
    internal void HandleRaidInstanceMessage(WorldPacket packet)
    {
        RaidInstanceMessage instance = new RaidInstanceMessage();
        instance.Type = (InstanceResetWarningType)packet.ReadUInt32();
        instance.MapID = packet.ReadUInt32();

        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056))
            instance.DifficultyID = (DifficultyModern)packet.ReadUInt32();
        else
        {
            if (ModernVersion.ExpansionVersion == 1)
                instance.DifficultyID = DifficultyModern.Raid40;
            else
                instance.DifficultyID = DifficultyModern.Raid25N;
        }

        instance.TimeLeft = packet.ReadInt32();

        if (LegacyVersion.AddedInVersion(ClientVersionBuild.V3_0_2_9056) &&
            instance.Type == InstanceResetWarningType.Welcome)
        {
            instance.Locked = packet.ReadBool();
            instance.Extended = packet.ReadBool();
        }

        SendPacketToClient(instance);
    }

    /// <summary>
    /// A boss's unit frame. A legacy server sends one opcode with a frame type first, a modern
    /// client has a packet for each type. The three that put a boss frame up, move it among the
    /// others and take it down are passed on; the timer and objective types are not.
    /// </summary>
    [HandlesSmsg(Opcode.SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT, RemovedIn = ClientVersionBuild.V4_0_1_13164)]
    internal void HandleUpdateInstanceEncounterUnit(WorldPacket packet)
    {
        switch ((EncounterFrameType)packet.ReadUInt32())
        {
            case EncounterFrameType.Engage:
                SendEncounterEngageUnit(packet);
                break;
            case EncounterFrameType.Disengage:
                SendEncounterDisengageUnit(packet);
                break;
            case EncounterFrameType.UpdatePriority:
                SendEncounterChangePriority(packet);
                break;
        }
    }

    /// <summary>
    /// The same on a 4.3.4 server, whose frame types are numbered differently. Its combat
    /// resurrection limit types are not passed on either.
    /// </summary>
    [HandlesSmsg(Opcode.SMSG_UPDATE_INSTANCE_ENCOUNTER_UNIT, AddedIn = ClientVersionBuild.V4_0_1_13164)]
    internal void HandleUpdateInstanceEncounterUnitCata(WorldPacket packet)
    {
        switch ((EncounterFrameTypeCata)packet.ReadUInt32())
        {
            case EncounterFrameTypeCata.Engage:
                SendEncounterEngageUnit(packet);
                break;
            case EncounterFrameTypeCata.Disengage:
                SendEncounterDisengageUnit(packet);
                break;
            case EncounterFrameTypeCata.UpdatePriority:
                SendEncounterChangePriority(packet);
                break;
        }
    }

    // Both legacy layouts follow the type with the boss's packed GUID and a frame priority byte,
    // the disengage one included; the modern disengage packet has no priority.
    private void SendEncounterEngageUnit(WorldPacket packet)
    {
        if (ModernVersion.GetCurrentOpcode(Opcode.SMSG_INSTANCE_ENCOUNTER_ENGAGE_UNIT) == 0)
            return;

        InstanceEncounterEngageUnit engage = new();
        engage.Unit = packet.ReadPackedGuid().To128(GetSession().GameState);
        engage.TargetFramePriority = packet.ReadUInt8();
        SendPacketToClient(engage);
    }

    private void SendEncounterDisengageUnit(WorldPacket packet)
    {
        if (ModernVersion.GetCurrentOpcode(Opcode.SMSG_INSTANCE_ENCOUNTER_DISENGAGE_UNIT) == 0)
            return;

        InstanceEncounterDisengageUnit disengage = new();
        disengage.Unit = packet.ReadPackedGuid().To128(GetSession().GameState);
        SendPacketToClient(disengage);
    }

    private void SendEncounterChangePriority(WorldPacket packet)
    {
        if (ModernVersion.GetCurrentOpcode(Opcode.SMSG_INSTANCE_ENCOUNTER_CHANGE_PRIORITY) == 0)
            return;

        InstanceEncounterChangePriority priority = new();
        priority.Unit = packet.ReadPackedGuid().To128(GetSession().GameState);
        priority.TargetFramePriority = packet.ReadUInt8();
        SendPacketToClient(priority);
    }
}
