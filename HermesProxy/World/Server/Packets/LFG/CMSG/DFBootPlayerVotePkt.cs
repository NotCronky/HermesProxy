namespace HermesProxy.World.Server.Packets;

/// <summary>
/// A member's answer to a vote to remove someone from the dungeon finder group: yes or no, one
/// bit (the 4.4.2 client's writer).
/// </summary>
public readonly record struct DFBootPlayerVotePkt(bool Vote);
