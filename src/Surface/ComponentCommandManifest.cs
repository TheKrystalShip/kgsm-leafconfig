using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// The commands a component declares, as its own manifest states them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Passed through, never modelled.</b> The manifest is a file format a component ships on disk and
/// every reader of it renders what it declares. A typed copy here would be a second statement of the
/// same schema, free to disagree with the file the build wrote — which is the whole failure this
/// library exists to end.
/// </para>
/// <para>
/// It is parsed before it is served, though. A malformed manifest reaching a browser is a rendering
/// failure with no explanation in it, and <see cref="ManifestRead.Unreadable"/> with a reason is the
/// honest answer instead.
/// </para>
/// </remarks>
public sealed class ComponentCommandManifest(
    ComponentSurfaceOptions options,
    ILogger<ComponentCommandManifest> logger)
{
    /// <summary>What a read found: the manifest's own bytes, nothing declared, or a file that is there
    /// and cannot be served.</summary>
    public enum ManifestRead
    {
        /// <summary>The manifest was read and parses.</summary>
        Ok,

        /// <summary>This component declares no commands, which is the ordinary state for most of them.</summary>
        None,

        /// <summary>A file is there and could not be read or does not parse.</summary>
        Unreadable,
    }

    /// <summary>
    /// The manifest's JSON verbatim, or why there is none. The caller writes <paramref name="json"/>
    /// through untouched — re-serializing it would mean holding the schema.
    /// </summary>
    public ManifestRead Read(out string? json, out string? why)
    {
        json = null;
        why = null;

        string? path = options.CommandsPath;
        if (string.IsNullOrWhiteSpace(path))
            return ManifestRead.None;

        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return ManifestRead.None;
        }
        catch (DirectoryNotFoundException)
        {
            return ManifestRead.None;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "could not read the command manifest at {Path}", path);
            why = "This component's command manifest could not be read on this host.";
            return ManifestRead.Unreadable;
        }

        try
        {
            using (JsonDocument.Parse(raw)) { }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "the command manifest at {Path} is not valid JSON", path);
            why = "This component's command manifest is on disk but is not valid JSON.";
            return ManifestRead.Unreadable;
        }

        json = raw;
        return ManifestRead.Ok;
    }
}
