using System;
using System.Globalization;
using System.Text.Json;

namespace BNetServer;

/// <summary>
/// The client's offset from UTC, as its Battle.net logon gives it: the <c>device_id</c> JSON has
/// <c>UTCO</c>, the 32-bit FNV-1a hash of the offset in minutes written as decimal text ("120",
/// "-300"). TrinityCore's bnetserver reads it the same way (Trinity::Timezone::GetOffsetByHash);
/// the modern clients want their calendar's dates in that offset.
/// </summary>
public static class ClientTimeZone
{
    // UTC-12:00 to UTC+14:00, the offsets a time zone has
    private const int MinMinutes = -12 * 60;
    private const int MaxMinutes = 14 * 60;

    public static TimeSpan? FromDeviceId(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return null;

        uint hash;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(deviceId);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("UTCO", out JsonElement utco)
                || !utco.TryGetUInt32(out hash))
                return null;
        }
        catch (JsonException)
        {
            return null;
        }

        return FromHash(hash);
    }

    public static TimeSpan? FromHash(uint hash)
    {
        for (int minutes = MinMinutes; minutes <= MaxMinutes; minutes++)
        {
            if (Fnv1a(minutes.ToString(CultureInfo.InvariantCulture)) == hash)
                return TimeSpan.FromMinutes(minutes);
        }
        return null;
    }

    internal static uint Fnv1a(string text)
    {
        uint hash = 0x811C9DC5u;
        foreach (char c in text)
        {
            hash ^= (byte)c;
            hash *= 0x1000193u;
        }
        return hash;
    }
}
