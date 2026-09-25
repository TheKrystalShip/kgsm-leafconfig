using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// What this host's deploy files set, and whether all of them could be read.
/// </summary>
/// <param name="Values">Env name → the value a deploy file sets it to.</param>
/// <param name="Complete">
/// False when a declared source is there and could not be read. Then a key absent from
/// <paramref name="Values"/> is genuinely <em>unknown</em> rather than "unset, so the coded default
/// applies" — and only the second of those licenses reporting the default as what is running.
/// </param>
public sealed record ComponentFloor(IReadOnlyDictionary<string, string> Values, bool Complete);

/// <summary>
/// What this host's deploy files set, before any override — the tier between the coded default and
/// what an administrator has changed.
/// </summary>
/// <remarks>
/// <para>
/// Reported so a person reading the page can tell three things apart that look identical when only the
/// value is shown: a knob left at what the code ships with, one the host's own deploy set, and one
/// somebody changed here. Without the middle tier, resetting an override looks like it will restore the
/// coded default when it will restore something else entirely.
/// </para>
/// <para>
/// The sources are the ones the descriptor declares, read in the order it declares them — lowest
/// precedence first, so the last writer wins, which is the order the component itself resolves them in.
/// A source that cannot be read contributes nothing and is not guessed at.
/// </para>
/// </remarks>
public sealed class ComponentFloorReader(ComponentSurfaceOptions options, ILogger<ComponentFloorReader> logger)
{
    private static readonly string[] UnitDirs =
    [
        "/etc/systemd/system",
        "/run/systemd/system",
        "/usr/lib/systemd/system",
        "/lib/systemd/system",
    ];

    /// <summary>
    /// Env name → the value this host's deploy files set it to, and whether every declared source was
    /// actually read.
    /// </summary>
    /// <remarks>
    /// <b>A key's absence means two different things, and the caller has to be able to tell them
    /// apart.</b> With a complete floor, a key that is not here is one nothing sets, so the coded
    /// default is what the component is running with. With an incomplete one it may be a key a source
    /// sets that could not be read — and reporting the default then states something nobody measured.
    /// </remarks>
    public ComponentFloor Read(IReadOnlyList<ComponentFloorSource> sources)
    {
        var floor = new Dictionary<string, string>(StringComparer.Ordinal);
        bool complete = true;

        foreach (ComponentFloorSource source in sources)
        {
            try
            {
                switch (source.Kind)
                {
                    case "appsettings": complete &= ReadJsonSettings(source.Path, floor); break;
                    case "env-file": complete &= ReadEnvFile(source.Path, floor); break;
                    case "systemd-unit": complete &= ReadUnit(source.Path, floor); break;

                    // A kind this build does not know contributes nothing, and says so: a source it
                    // cannot read is a source it cannot read, whatever the reason.
                    default: complete = false; break;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "floor source {Kind} at {Path} could not be read", source.Kind, source.Path);
                complete = false;
            }
        }

        return new ComponentFloor(floor, complete);
    }

    /// <summary>
    /// The settings file, flattened to env names. <c>{"Monitor":{"IntervalMs":1000}}</c> is what
    /// <c>Monitor__IntervalMs</c> binds from, so the two are the same fact spelled two ways and the
    /// flattening is what lets one table hold both.
    /// </summary>
    /// <returns>
    /// False when the file is there and could not be read or parsed. Absent is not a failure — a
    /// component may genuinely ship without one.
    /// </returns>
    private bool ReadJsonSettings(string path, Dictionary<string, string> into)
    {
        if (!File.Exists(path))
            return true;

        try
        {
            // Comments and trailing commas, because Microsoft.Extensions.Configuration's own JSON
            // provider accepts them — a component whose settings file is annotated is reading it fine,
            // and rejecting it here would report that component's whole floor as unknown over
            // punctuation.
            using FileStream stream = File.OpenRead(path);
            using JsonDocument doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            Walk(doc.RootElement, "", into);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "settings file {Path} is there and could not be read", path);
            return false;
        }
    }

    private static void Walk(JsonElement element, string prefix, Dictionary<string, string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty p in element.EnumerateObject())
                    Walk(p.Value, prefix.Length == 0 ? p.Name : prefix + "__" + p.Name, into);
                break;

            // A list binds by index, which no descriptor field names — so it contributes nothing rather
            // than a key nothing could override.
            case JsonValueKind.Array:
            case JsonValueKind.Null:
                break;

            // A JSON boolean, spelled the way the descriptor and every other tier spell one. Left to
            // JsonElement.ToString() it arrives as "True", which is not a value any component's parser
            // writes and not what the field's default says — so a surface comparing this floor against
            // a default of "true" finds them different and draws a switch that is ON as off. That is a
            // surface misreporting what the component is running with, which is the one thing the
            // provenance view exists not to do.
            case JsonValueKind.True or JsonValueKind.False:
                if (prefix.Length > 0)
                    into[prefix] = element.ValueKind == JsonValueKind.True ? "true" : "false";
                break;

            default:
                if (prefix.Length > 0)
                    into[prefix] = element.ToString();
                break;
        }
    }

    /// <summary>
    /// An env file, in systemd's own reading of one: <c>KEY=value</c>, blanks and <c>#</c> comments
    /// skipped. The override file is passed over wherever it appears — it is the tier ABOVE this one,
    /// and counting it here would report every override as something the host's deploy had set.
    /// </summary>
    /// <returns>
    /// False when the file is there and could not be read. An absent file is not a failure: "nothing
    /// sets these keys" is a fact, and systemd tolerates the same absence.
    /// </returns>
    private bool ReadEnvFile(string path, Dictionary<string, string> into)
    {
        if (!File.Exists(path) || SameFile(path, options.OverridePath))
            return true;

        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "env file {Path} is there and could not be read", path);
            return false;
        }

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;

            int eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;

            into[trimmed[..eq].Trim()] = Unquote(trimmed[(eq + 1)..].Trim());
        }

        return true;
    }

    /// <summary>
    /// The unit, read the way systemd reads it: <c>Environment=</c> sets a value directly and
    /// <c>EnvironmentFile=</c> pulls one in, both in file order, and a drop-in is appended after the
    /// unit itself. Following the referenced files is what makes the reported floor the real one — a
    /// unit commonly sets its keys through shared files under <c>/etc/kgsm</c>, and a reader that
    /// stopped at <c>Environment=</c> would report a coded default that is not in force.
    /// </summary>
    /// <returns>
    /// False when the unit itself was found nowhere, or when a fragment could not be read. A unit this
    /// component cannot find is one whose <c>Environment=</c> lines are unknown rather than empty.
    /// </returns>
    private bool ReadUnit(string unitName, Dictionary<string, string> into)
    {
        bool found = false;
        bool complete = true;

        foreach (string dir in UnitDirs)
        {
            string unit = Path.Combine(dir, unitName);
            if (File.Exists(unit))
            {
                found = true;
                complete &= ReadUnitFile(unit, into);
            }

            string dropInDir = Path.Combine(dir, unitName + ".d");
            if (!Directory.Exists(dropInDir))
                continue;

            foreach (string conf in Directory.GetFiles(dropInDir, "*.conf").OrderBy(f => f, StringComparer.Ordinal))
                complete &= ReadUnitFile(conf, into);
        }

        return found && complete;
    }

    private bool ReadUnitFile(string path, Dictionary<string, string> into)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "unit fragment {Path} could not be read", path);
            return false;
        }

        bool complete = true;

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';')
                continue;

            if (trimmed.StartsWith("Environment=", StringComparison.Ordinal))
            {
                string assignment = trimmed["Environment=".Length..].Trim();
                int eq = assignment.IndexOf('=');
                if (eq > 0)
                    into[assignment[..eq].Trim()] = Unquote(assignment[(eq + 1)..].Trim());
                continue;
            }

            if (trimmed.StartsWith("EnvironmentFile=", StringComparison.Ordinal))
            {
                string file = trimmed["EnvironmentFile=".Length..].Trim();
                // A leading '-' means "absent is fine", which is a statement about the file rather than
                // about its contents — so it changes nothing here: an absent file is not a failure
                // either way, and one that is there and unreadable is one either way.
                complete &= ReadEnvFile(file.TrimStart('-'), into);
            }
        }

        return complete;
    }

    private static bool SameFile(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(b))
            return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.Ordinal); }
        catch { return false; }
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
}
