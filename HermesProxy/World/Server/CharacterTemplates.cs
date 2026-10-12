using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Framework.Logging;
using HermesProxy.World.Logging;
using static HermesProxy.World.Server.Packets.AuthResponse;

namespace HermesProxy.World.Server;

/// <summary>
/// Character templates for servers that apply them themselves (wow-progressive's mod-progressive).
/// <para>
/// <c>CharacterTemplates.json</c> in the proxy's folder lists each realm's templates by realm name. The auth
/// response offers them, and the client then shows its template frame on character select. A legacy
/// <c>CMSG_CHAR_CREATE</c> has no template field, so the set id the client chose is appended to the forwarded
/// packet behind the marker <c>PTPL</c> (<see cref="AppendToLegacyCreate"/>); a server that doesn't look for it
/// reads the fields it knows and ignores the rest.
/// </para>
/// <para>
/// The file is read again whenever it changes, so regenerating it needs no proxy restart. No file, or no entry for
/// the realm, offers no templates.
/// </para>
/// </summary>
internal static class CharacterTemplates
{
    private const string FileName = "CharacterTemplates.json";
    private const int MaxNameBytes = 127;           // 7-bit length in the auth response
    private const int MaxDescriptionBytes = 1023;   // 10-bit length

    private static readonly byte[] LegacyCreateMarker = "PTPL"u8.ToArray();
    private static readonly Microsoft.Extensions.Logging.ILogger _melLog = Log.CreateMelLogger(Log.CategoryServer);
    private static readonly string _sourceFile = nameof(CharacterTemplates).PadRight(15);
    private static readonly object _lock = new();

    private static DateTime _loadedWriteTime;
    private static Dictionary<string, List<CharacterTemplateEntry>> _byRealm = [];

    public static void AddTemplates(AuthSuccessInfo info, string realmName)
    {
        var entries = GetEntries(realmName);
        if (entries == null)
            return;

        foreach (var entry in entries)
        {
            var template = new CharacterTemplate
            {
                TemplateSetId = entry.Id,
                Name = Truncate(entry.Name, MaxNameBytes),
                Description = Truncate(entry.Description, MaxDescriptionBytes),
            };
            // The client matches each class entry against the race's faction group, which includes the Player
            // bit: one entry per faction (3 Alliance, 5 Horde). Alliance | Horde alone matches no race.
            foreach (byte classId in entry.Classes)
            {
                template.Classes.Add(new CharacterTemplateClass(FactionMasks.Player | FactionMasks.Alliance, classId));
                template.Classes.Add(new CharacterTemplateClass(FactionMasks.Player | FactionMasks.Horde, classId));
            }
            info.Templates.Add(template);
        }
        CharacterEnumLogMessages.TemplatesOffered(_melLog, _sourceFile, info.Templates.Count, realmName);
    }

    public static void AppendToLegacyCreate(WorldPacket packet, string name, uint? templateSet)
    {
        if (templateSet is not uint id)
            return;

        packet.WriteBytes(LegacyCreateMarker);
        packet.WriteUInt32(id);
        CharacterEnumLogMessages.TemplateCreate(_melLog, _sourceFile, name, id);
    }

    private static List<CharacterTemplateEntry>? GetEntries(string realmName)
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FileName))
                {
                    _byRealm = [];
                    _loadedWriteTime = default;
                    return null;
                }

                var writeTime = File.GetLastWriteTimeUtc(FileName);
                if (writeTime != _loadedWriteTime)
                {
                    _byRealm = JsonSerializer.Deserialize(File.ReadAllText(FileName, Encoding.UTF8),
                        HermesJsonContext.Default.DictionaryStringListCharacterTemplateEntry) ?? [];
                    _loadedWriteTime = writeTime;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                CharacterEnumLogMessages.TemplatesLoadFailed(_melLog, _sourceFile, FileName, ex.Message);
                return null;
            }

            return _byRealm.TryGetValue(realmName, out var entries) ? entries : null;
        }
    }

    private static string Truncate(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            return text;
        while (text.Length > 0 && Encoding.UTF8.GetByteCount(text) > maxBytes)
            text = text[..^1];
        return text;
    }
}

/// <summary>One template in <c>CharacterTemplates.json</c>: the set id the server decodes, and the classes it
/// allows (both factions).</summary>
public sealed class CharacterTemplateEntry
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<byte> Classes { get; set; } = [];
}
