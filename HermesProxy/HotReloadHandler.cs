#if DEBUG
using System;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using Framework.Logging;
using HermesProxy.World.Dispatch;

[assembly: MetadataUpdateHandler(typeof(HermesProxy.HotReloadHandler))]

namespace HermesProxy;

/// <summary>
/// Finishes a hot reload (<c>dotnet watch</c>) for the dispatch tables, which are built once.
/// </summary>
/// <remarks>
/// <para>
/// A hot reload patches method bodies in the running process. It cannot run a static initializer a
/// second time: <c>TypeInitializer.Invoke</c> and <c>RuntimeHelpers.RunClassConstructor</c> both
/// return without doing anything once the type is initialized. The generated dispatch tables are
/// filled by one, so a handler given a <c>[HandlesCmsg]</c> / <c>[HandlesSmsg]</c> while the proxy
/// runs would be patched in without complaint and never called. The runtime calls
/// <see cref="UpdateApplication"/> after every patch, and it builds both tables again.
/// </para>
/// <para>
/// Nothing else needs this. An edit to a static field's own initializer (a packet's list of
/// layouts, a lookup table) is reported by the compiler as ENC0118 and <c>dotnet watch</c>
/// restarts the proxy for it. The tables are the case it cannot see: their initializer never
/// changes, only the method it calls.
/// </para>
/// <para>
/// Debug builds only, because the runtime takes patches for unoptimized assemblies alone.
/// </para>
/// </remarks>
internal static class HotReloadHandler
{
    private static readonly Microsoft.Extensions.Logging.ILogger _melServer = Log.CreateMelLogger(Log.CategoryServer);
    private static readonly string _sourceFile = nameof(HotReloadHandler).PadRight(15);
    private const string _netDirNone = "";
    private const int MaxTypesNamed = 6;

    private static int _updates;

    /// <summary>
    /// Found by name through <see cref="MetadataUpdateHandlerAttribute"/>.
    /// <paramref name="updatedTypes"/> is null when the runtime cannot say what changed.
    /// </summary>
    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        int number = Interlocked.Increment(ref _updates);

        // After every patch, not only one that names a system or a handler class: the tables are
        // generated code, and which edit moves them is the generator's business.
        GeneratedCmsgDispatch.Rebuild();
        GeneratedSmsgDispatch.Rebuild();

        ServerLogMessages.HotReloadApplied(_melServer, _sourceFile, _netDirNone, number, Describe(updatedTypes));
    }

    private static string Describe(Type[]? updatedTypes)
    {
        if (updatedTypes is null)
            return "unknown types";

        // The compiler's own types ride along with most patches (<PrivateImplementationDetails>,
        // __StaticArrayInitTypeSize=…, the interceptor holders) and say nothing about the edit.
        string[] written = updatedTypes
            .Select(static type => type.Name)
            .Where(static name => name.Length > 0 && name[0] != '<' && !name.StartsWith("__", StringComparison.Ordinal))
            .ToArray();
        if (written.Length == 0)
            return "generated code only";

        string names = string.Join(", ", written.Take(MaxTypesNamed));
        return written.Length > MaxTypesNamed ? $"{names} and {written.Length - MaxTypesNamed} more" : names;
    }
}
#endif
