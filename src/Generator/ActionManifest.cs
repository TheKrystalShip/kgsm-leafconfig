using System.Text.Encodings.Web;
using System.Text.Json;

namespace TheKrystalShip.KGSM.ComponentConfig.Gen;

/// <summary>One action a component performs, as its manifest lists it.</summary>
/// <param name="Id">The local id: <c>rules.write</c>. The manifest's <c>component</c> is the other half.</param>
/// <param name="DeclaredAt">Where it was declared, for a message; never written.</param>
internal sealed record ActionDef(string Id, string Title, string Effect, string Scope, bool Self, string? DeclaredAt = null);

/// <summary>Another component's action this one performs as its own service account.</summary>
internal sealed record RequirementDef(string Action, string Scope, string Why);

/// <summary>What a component declares about actions, before the standard surface is added.</summary>
internal sealed record ActionSurface(
    string Component,
    string Version,
    IReadOnlyList<ActionDef> Actions,
    IReadOnlyList<RequirementDef> Requires);

/// <summary>
/// The actions every component's standard surface performs — its configuration, its journal and its
/// lifecycle, served through <c>ComponentSurface</c>. Declared here, by the package, so no component
/// writes them.
/// </summary>
/// <remarks>
/// Their scope is where the component is administered: the node for a leaf, the cluster for an anchor.
/// The ids are <c>ComponentSurfaceActions</c>' in the surface package, and a test holds the two lists
/// together.
/// </remarks>
internal static class Standard
{
    internal const string ConfigRead = "config.read";
    internal const string ConfigWrite = "config.write";
    internal const string JournalRead = "journal.read";
    internal const string LifecycleRestart = "lifecycle.restart";

    internal static readonly string[] Ids = [ConfigRead, ConfigWrite, JournalRead, LifecycleRestart];

    public static IEnumerable<ActionDef> For(ComponentIdentity identity, ComponentKind written)
    {
        string scope = written == ComponentKind.Anchor ? "cluster" : "node";
        string name = identity.DisplayName;

        yield return new ActionDef(ConfigRead, $"Read {name} settings", "read", scope, false);
        yield return new ActionDef(ConfigWrite, $"Change {name} settings", "write", scope, false);
        yield return new ActionDef(JournalRead, $"Read {name} logs", "read", scope, false);
        yield return new ActionDef(LifecycleRestart, $"Restart {name}", "execute", scope, false);
    }
}

/// <summary>
/// Writes the action manifest beside a component's descriptor. Key order is fixed for the same reason
/// the descriptor's is: it is reviewed as a diff.
/// </summary>
internal static class ActionManifestEmitter
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Where the manifest for a descriptor goes: the descriptor's name with <c>.json</c> made
    /// <c>.actions.json</c>, so it carries the same <c>.leaf</c>/<c>.anchor</c> suffix the deploy
    /// routes on.
    /// </summary>
    public static string PathFor(string descriptorPath) =>
        descriptorPath.EndsWith(".json", StringComparison.Ordinal)
            ? descriptorPath[..^".json".Length] + Names.Manifest.Suffix
            : descriptorPath + Names.Manifest.Suffix;

    public static string Render(ActionSurface surface, ComponentIdentity identity, ComponentKind written)
    {
        IEnumerable<ActionDef> actions = Standard.For(identity, written)
            .Concat(surface.Actions)
            .OrderBy(a => a.Id, StringComparer.Ordinal);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            writer.WriteStartObject();
            writer.WriteNumber(Names.Json.SchemaVersion, Names.Manifest.SchemaVersion);
            writer.WriteString(Names.Manifest.Component, surface.Component);
            writer.WriteString(Names.Manifest.Version, surface.Version);

            writer.WriteStartArray(Names.Manifest.Actions);
            foreach (ActionDef action in actions)
            {
                writer.WriteStartObject();
                writer.WriteString(Names.Manifest.Id, action.Id);
                writer.WriteString(Names.Manifest.Title, action.Title);
                writer.WriteString(Names.Manifest.Effect, action.Effect);
                writer.WriteString(Names.Manifest.Scope, action.Scope);

                // Omitted rather than written false: absence is "acts on more than the caller's own".
                if (action.Self)
                    writer.WriteBoolean(Names.Manifest.Self, true);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray(Names.Manifest.Requires);
            foreach (RequirementDef requirement in surface.Requires)
            {
                writer.WriteStartObject();
                writer.WriteString(Names.Manifest.Action, requirement.Action);
                writer.WriteString(Names.Manifest.Scope, requirement.Scope);
                writer.WriteString(Names.Manifest.Why, requirement.Why);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
