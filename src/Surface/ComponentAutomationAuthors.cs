using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>Who last set a setting that switches automated behaviour on, and when.</summary>
/// <param name="Author">The account that set it.</param>
/// <param name="Set">When.</param>
public sealed record AutomationAuthor(
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("set")] DateTimeOffset Set);

/// <summary>
/// The author of each setting a component marks <c>[Automates]</c>: the person who switched that
/// automated behaviour on, recorded beside the value when it is written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every automation has a person author.</b> A service firing on its own later acts only where both
/// its own service account and this author may act, so writing a setting never lends its author the
/// service's reach, and an author who loses access stops what they switched on. The component reads the
/// author here when it fires; a setting with no author recorded is blocked.
/// </para>
/// <para>
/// <b>Beside the override file, and written after it.</b> The value and its author are one statement,
/// kept in the same directory with the same mode. A value written with nobody to name — a change with no
/// caller the surface can identify — clears the author rather than keeping the last one, so a setting is
/// never attributed to somebody who did not set it. Resetting a setting to its floor clears it too.
/// </para>
/// </remarks>
public sealed class ComponentAutomationAuthors(ComponentSurfaceOptions options, ILogger<ComponentAutomationAuthors> logger)
{
    private const UnixFileMode File0600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>The file the authors are kept in.</summary>
    public string Path => options.OverridePath + ".authors.json";

    /// <summary>Every recorded author, by descriptor key. Read from the file on every call.</summary>
    public IReadOnlyDictionary<string, AutomationAuthor> Read()
    {
        if (!File.Exists(Path))
            return new Dictionary<string, AutomationAuthor>(StringComparer.Ordinal);

        try
        {
            using FileStream stream = File.OpenRead(Path);
            Dictionary<string, AutomationAuthor>? authors =
                JsonSerializer.Deserialize(stream, ComponentAutomationAuthorsJson.Default.DictionaryStringAutomationAuthor);
            return authors is null
                ? new Dictionary<string, AutomationAuthor>(StringComparer.Ordinal)
                : new Dictionary<string, AutomationAuthor>(authors, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable reads as nobody: every automation it held is blocked, never run on a guess.
            logger.LogWarning(ex, "could not read the automation authors at {Path}", Path);
            return new Dictionary<string, AutomationAuthor>(StringComparer.Ordinal);
        }
    }

    /// <summary>The account that switched <paramref name="key"/> on, or null when nobody is recorded.</summary>
    public string? AuthorOf(string key) => Read().TryGetValue(key, out AutomationAuthor? author) ? author.Author : null;

    /// <summary>
    /// Record <paramref name="author"/> for every key in <paramref name="set"/>, and clear every key in
    /// <paramref name="cleared"/>. A null author clears the keys it set.
    /// </summary>
    public void Record(IEnumerable<string> set, IEnumerable<string> cleared, string? author, DateTimeOffset now)
    {
        var authors = new Dictionary<string, AutomationAuthor>(Read(), StringComparer.Ordinal);
        bool changed = false;

        foreach (string key in set)
        {
            if (author is null)
                changed |= authors.Remove(key);
            else
            {
                authors[key] = new AutomationAuthor(author, now);
                changed = true;
            }
        }

        foreach (string key in cleared)
            changed |= authors.Remove(key);

        if (!changed)
            return;

        if (authors.Count == 0)
        {
            try { File.Delete(Path); }
            catch (IOException ex) { logger.LogWarning(ex, "could not remove the automation authors at {Path}", Path); }
            return;
        }

        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(
            authors.OrderBy(a => a.Key, StringComparer.Ordinal).ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal),
            ComponentAutomationAuthorsJson.Default.DictionaryStringAutomationAuthor));
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(temp, File0600);
        File.Move(temp, Path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, AutomationAuthor>))]
internal sealed partial class ComponentAutomationAuthorsJson : JsonSerializerContext;
