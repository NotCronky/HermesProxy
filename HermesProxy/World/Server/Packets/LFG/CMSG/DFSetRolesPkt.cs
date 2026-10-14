namespace HermesProxy.World.Server.Packets;

/// <remarks>An optional PartyIndex byte follows the roles on the wire and is not kept.</remarks>
public readonly record struct DFSetRolesPkt(byte Roles);
