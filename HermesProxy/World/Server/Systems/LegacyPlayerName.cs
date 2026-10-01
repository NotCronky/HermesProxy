using System;

namespace HermesProxy.World.Server.Systems;

/// <summary>
/// Turns a player name the modern client sends into the form a legacy core looks up.
/// </summary>
internal static class LegacyPlayerName
{
    /// <summary>
    /// Cuts a modern <c>Name-Realm</c> target down to the bare name, as a slice of
    /// <paramref name="name"/> so the cut never allocates.
    /// </summary>
    /// <remarks>
    /// The client appends the realm whenever it believes the player is on another realm, and a
    /// realm it cannot resolve leaves only the hyphen: <c>"Name-"</c>, seen on a whisper in issue
    /// #335. The chat right-click menu builds the Add Friend, Ignore and Guild Invite targets with
    /// the same Lua helper (<c>UnitPopupSharedUtil.GetFullPlayerName</c>). Modern TrinityCore cuts
    /// whisper targets at the first <c>'-'</c> (<c>ExtractExtendedPlayerName</c>); legacy cores
    /// compare the whole string and answer "player not found". Legacy character names cannot
    /// contain <c>'-'</c>, so the cut is safe on every backend.
    /// </remarks>
    public static ReadOnlySpan<char> StripRealmSuffix(string name)
    {
        int dash = name.IndexOf('-');
        return dash < 0 ? name : name.AsSpan(0, dash);
    }

    /// <summary>
    /// <see cref="StripRealmSuffix"/> for callers that need a string: the chat path hands the
    /// target to <c>WorldClient.SendMessageChat*</c>, which takes one. Returns
    /// <paramref name="name"/> itself when there is nothing to cut, so only a name that carried a
    /// suffix allocates.
    /// </summary>
    public static string StripRealmSuffixToString(string name)
    {
        ReadOnlySpan<char> bare = StripRealmSuffix(name);
        return bare.Length == name.Length ? name : bare.ToString();
    }
}
