using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TheKrystalShip.Api.Contracts;

namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// What systemd reports about this component's own unit, as the row every Services surface renders.
/// </summary>
/// <remarks>
/// <para>
/// A node's API reads this for each of its leaves; a component with no node above it reads its own, the
/// same way it reads its own journal and serves its own configuration. The answer is a
/// <see cref="ComponentService"/> either way, so one panel component renders both.
/// </para>
/// <para>
/// <b>Every field is honest.</b> An unmeasured value is null and a unit that could not be read at all is
/// <c>unknown</c> — never a fabricated "running", "stopped" or "0 bytes". <c>not-found</c> and
/// <c>masked</c> are load-time facts that override the active state, because a unit that is not installed
/// is not a unit that is stopped.
/// </para>
/// <para>
/// <b>Memory is the main process's own cgroup, not systemd's <c>MemoryCurrent</c>.</b> systemd reports the
/// unit subtree's total, which charges a supervised workload's memory to its supervisor — a different
/// quantity, not a degraded version of this one. Like <c>memory.current</c> everywhere else in the
/// ecosystem it counts reclaimable page cache, so it sits above the process's RSS.
/// </para>
/// <para>
/// <b>Health is operational because the component is answering.</b> Anything serving this row is by
/// definition reachable; there is nothing further to probe and nothing honest to claim beyond it.
/// </para>
/// </remarks>
public sealed class ComponentUnitReader(
    ComponentDescriptorStore descriptors,
    ILogger<ComponentUnitReader> logger)
{
    private const string Systemctl = "/usr/bin/systemctl";
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(4);

    // Single-line scalars, so the Key=Value parse below is unambiguous. MemoryCurrent is absent on
    // purpose — see the remarks.
    private static readonly string[] Properties =
        ["Id", "LoadState", "ActiveState", "SubState", "UnitFileState", "MainPID", "ActiveEnterTimestamp"];

    /// <summary>
    /// This component's own services row, or null when it has no descriptor and therefore no unit to
    /// report on.
    /// </summary>
    public async Task<ComponentService?> ReadAsync(CancellationToken ct = default)
    {
        if (descriptors.Current() is not { } descriptor)
            return null;

        Dictionary<string, string> block = await ShowAsync(descriptor.Unit, ct).ConfigureAwait(false);

        string load = Get(block, "LoadState");
        string active = Get(block, "ActiveState");
        string sub = Get(block, "SubState");

        string state =
            block.Count == 0 ? "unknown" :
            load == "not-found" ? "not-installed" :
            load == "masked" ? "masked" :
            active.Length > 0 ? active :
            "unknown";

        bool? enabled = Get(block, "UnitFileState") switch
        {
            "enabled" or "enabled-runtime" => true,
            "disabled" => false,
            // static / indirect / generated / transient / masked / "" — enablement does not apply, or is
            // not known. Never guessed at either way.
            _ => null,
        };

        int? pid = ParsePid(Get(block, "MainPID"));

        return new ComponentService(
            Id: descriptor.Id,
            DisplayName: descriptor.DisplayName,
            Role: descriptor.Role,
            Unit: descriptor.Unit,
            State: state,
            OnDemand: descriptor.OnDemand,
            // A component serving its own row holds no stored link for anything to arm or disarm — that
            // axis belongs to an API that keeps a connection to something else. Null renders as "not
            // applicable" rather than as "disconnected".
            Provisioned: null,
            SubState: sub.Length > 0 ? sub : null,
            Enabled: enabled,
            Since: ParseUnixStamp(Get(block, "ActiveEnterTimestamp")),
            MainPid: pid,
            MemoryBytes: pid is { } p ? CgroupMemory(p) : null,
            Health: new ComponentServiceHealth(CapabilityStatus.Operational, null));
    }

    private async Task<Dictionary<string, string>> ShowAsync(string unit, CancellationToken ct)
    {
        var block = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(Systemctl))
            return block;

        try
        {
            var psi = new ProcessStartInfo(Systemctl)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add("--timestamp=unix");
            psi.ArgumentList.Add("--property=" + string.Join(',', Properties));
            psi.ArgumentList.Add(unit);

            using var proc = new Process { StartInfo = psi };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ReadTimeout);

            if (!proc.Start())
                return block;

            string stdout;
            try
            {
                stdout = await proc.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
                await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                if (!proc.HasExited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* race: exited */ }
                }
            }

            foreach (string raw in stdout.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int eq = line.IndexOf('=');
                if (eq > 0)
                    block[line[..eq]] = line[(eq + 1)..];
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // the caller left — propagate rather than reporting an unknown unit
        }
        catch (Exception ex)
        {
            // Missing binary, no permission, a parse storm: the unit stays unknown, which is honest.
            logger.LogDebug(ex, "could not read systemd state for {Unit}", unit);
            block.Clear();
        }

        return block;
    }

    /// <summary>
    /// The memory charged to this process's own cgroup. Both files are world-readable, so this needs no
    /// privilege; anything unreadable is null, because the process exiting mid-read is ordinary and "not
    /// measured" is the honest answer.
    /// </summary>
    private static long? CgroupMemory(int pid)
    {
        try
        {
            string cgroup = File.ReadAllText($"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/cgroup");

            // The unified hierarchy's line is "0::<path>". A pid in the root cgroup exposes no
            // memory.current, which is why the path has to be non-empty rather than merely present.
            string? relative = null;
            foreach (string raw in cgroup.Split('\n'))
            {
                if (raw.StartsWith("0::", StringComparison.Ordinal))
                {
                    relative = raw[3..].Trim().TrimStart('/');
                    break;
                }
            }

            if (string.IsNullOrEmpty(relative))
                return null;

            string text = File.ReadAllText(Path.Combine("/sys/fs/cgroup", relative, "memory.current"));
            return long.TryParse(text.AsSpan().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out long bytes) ? bytes : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Get(IReadOnlyDictionary<string, string> b, string key) =>
        b.TryGetValue(key, out string? v) ? v : "";

    // --timestamp=unix renders as "@<epoch-seconds>", empty when the unit has never been active.
    private static DateTimeOffset? ParseUnixStamp(string v)
    {
        if (string.IsNullOrEmpty(v))
            return null;

        string s = v[0] == '@' ? v[1..] : v;
        int dot = s.IndexOf('.');
        if (dot >= 0)
            s = s[..dot];

        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long secs) && secs > 0
            ? DateTimeOffset.FromUnixTimeSeconds(secs)
            : null;
    }

    // MainPID=0 means "no main process" → null (not running), never a fake pid.
    private static int? ParsePid(string v) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : null;
}
