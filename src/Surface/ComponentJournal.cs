using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.Api.Contracts;

namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// Reads a component's own journal, by shelling <c>journalctl</c> for the unit its descriptor names.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit is the descriptor's</b>, not a second setting: the descriptor already names the unit
/// that carries this component, and two records of one fact disagree the first time one is edited.
/// </para>
/// <para>
/// <b>Reading a journal has to be granted.</b> Without membership of <c>systemd-journal</c>,
/// <c>journalctl</c> exits 0 having printed nothing — a success that looks exactly like a unit which
/// has logged nothing — so the unit carries <c>SupplementaryGroups=systemd-journal</c> and an empty
/// read is reported as empty rather than dressed up.
/// </para>
/// <para>
/// The lines are <see cref="LogLine"/>, the shape every KGSM log surface renders, so the panel's
/// console shows a component's journal identically wherever it was read from.
/// </para>
/// </remarks>
public sealed class ComponentJournal(ComponentDescriptorStore descriptors, ILogger<ComponentJournal> logger)
{
    private const string Journalctl = "/usr/bin/journalctl";
    private const int MaxLines = 2000;
    private const int DefaultLines = 300;

    /// <summary>
    /// The last <paramref name="lines"/> of this unit's journal, oldest first. Null when there is no
    /// unit to read or no journalctl to read it with — which is a different answer from a unit that has
    /// logged nothing, and is reported as one.
    /// </summary>
    public IReadOnlyList<LogLine>? Read(int? lines)
    {
        if (descriptors.Current() is not { Unit.Length: > 0 } descriptor)
            return null;

        if (!File.Exists(Journalctl))
            return null;

        int count = Math.Clamp(lines ?? DefaultLines, 1, MaxLines);

        try
        {
            var psi = new ProcessStartInfo(Journalctl)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(descriptor.Unit);
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add(count.ToString());
            psi.ArgumentList.Add("--no-pager");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("json");

            using Process? proc = Process.Start(psi);
            if (proc is null)
                return null;

            var read = new List<LogLine>(count);
            while (proc.StandardOutput.ReadLine() is { } raw)
            {
                if (Parse(raw, descriptor.Id) is { } line)
                    read.Add(line);
            }

            if (!proc.WaitForExit(10000))
            {
                logger.LogWarning("journalctl did not finish reading {Unit}", descriptor.Unit);
                return null;
            }

            return read;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "could not read the journal for {Unit}", descriptor.Unit);
            return null;
        }
    }

    /// <summary>
    /// One journald record → one line. Parsed with <see cref="JsonDocument"/> rather than a generated
    /// type because the fields wanted are three of many and their names are journald's, not a shape
    /// worth declaring — and because it costs an AOT build no reflection either way.
    /// </summary>
    /// <remarks>
    /// The live follow parses through this too, so a line reads the same whether it arrived over the
    /// read or the stream — two parsers would drift on the first field either of them learned about.
    /// </remarks>
    internal static LogLine? Parse(string raw, string source)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(raw);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("MESSAGE", out JsonElement message))
                return null;

            // journald stamps microseconds since the epoch, as a string.
            DateTimeOffset at =
                root.TryGetProperty("__REALTIME_TIMESTAMP", out JsonElement stamp)
                && long.TryParse(stamp.GetString(), out long micros)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(micros / 1000)
                    : DateTimeOffset.UnixEpoch;

            string priority = root.TryGetProperty("PRIORITY", out JsonElement p) ? p.GetString() ?? "6" : "6";
            string cursor = root.TryGetProperty("__CURSOR", out JsonElement c)
                ? c.GetString() ?? at.ToString("o")
                : at.ToString("o");

            return new LogLine(cursor, at, source, LevelOf(priority), Text(message));
        }
        catch
        {
            // A record this build cannot read is dropped rather than shown as a line of noise. It is one
            // line of a journal, and failing the whole read over it would lose the rest.
            return null;
        }
    }

    /// <summary>A MESSAGE is a string, or an array of bytes when it is not valid UTF-8.</summary>
    private static string Text(JsonElement message)
    {
        if (message.ValueKind == JsonValueKind.String)
            return message.GetString() ?? "";

        if (message.ValueKind != JsonValueKind.Array)
            return "";

        var bytes = new List<byte>();
        foreach (JsonElement b in message.EnumerateArray())
        {
            if (b.TryGetByte(out byte value))
                bytes.Add(value);
        }
        return System.Text.Encoding.UTF8.GetString([.. bytes]);
    }

    /// <summary>Syslog priority → the level word every KGSM log surface renders.</summary>
    private static string LevelOf(string priority) => priority switch
    {
        "0" or "1" or "2" or "3" => "error",
        "4" => "warn",
        "7" => "debug",
        _ => "info",
    };
}
