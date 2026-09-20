using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TheKrystalShip.Api.Contracts;

namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// The live tail of a component's journal: <b>one</b> <c>journalctl -f</c> for however many people are
/// watching.
/// </summary>
/// <remarks>
/// <para>
/// <b>One follow, not one per viewer.</b> Three browsers on this page must not be three processes
/// reading the same journal — the first subscriber starts it, the last one to leave kills it, and an
/// unwatched page costs nothing.
/// </para>
/// <para>
/// <b><c>-n 0</c>, so the follow carries no backlog.</b> A viewer hydrates its scrollback with
/// <see cref="ComponentJournal.Read"/> and applies live lines from the next one on. Sending history here
/// as well would show every line twice on every attach.
/// </para>
/// <para>
/// <b>A slow viewer drops lines rather than holding anybody up.</b> Each subscriber has a bounded queue
/// that discards its oldest when it fills, so one browser on a bad connection cannot stall the follow for
/// the rest — and the journal is the durable record, which a reconnect re-reads.
/// </para>
/// <para>
/// <b>Transport-free.</b> This hands out a reader of lines; framing them — as SSE on an anchor's HTTP
/// origin, or as NDJSON over the socket a leaf already serves — belongs to whoever is serving, which is
/// the one thing that differs between the two.
/// </para>
/// </remarks>
public sealed class ComponentJournalFollower(
    ComponentDescriptorStore descriptors,
    ILogger<ComponentJournalFollower> logger) : IDisposable
{
    private const string Journalctl = "/usr/bin/journalctl";
    private const int QueueDepth = 500;

    private readonly Lock _gate = new();
    private readonly List<Channel<LogLine>> _subscribers = [];
    private Process? _follow;
    private CancellationTokenSource? _stop;

    /// <summary>
    /// Watch the journal. The reader is the caller's to drain; disposing the returned handle leaves, and
    /// the last one to leave stops the follow. Null when there is no unit to follow or no journalctl to
    /// follow it with.
    /// </summary>
    public (ChannelReader<LogLine> Lines, IDisposable Handle)? Watch()
    {
        if (descriptors.Current() is not { Unit.Length: > 0 } || !File.Exists(Journalctl))
            return null;

        var channel = Channel.CreateBounded<LogLine>(new BoundedChannelOptions(QueueDepth)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        lock (_gate)
        {
            _subscribers.Add(channel);
            if (_subscribers.Count == 1)
                Start();
        }

        return (channel.Reader, new Leaver(this, channel));
    }

    private void Leave(Channel<LogLine> channel)
    {
        lock (_gate)
        {
            if (!_subscribers.Remove(channel))
                return;

            channel.Writer.TryComplete();
            if (_subscribers.Count == 0)
                Stop();
        }
    }

    /// <summary>Called under the lock.</summary>
    private void Start()
    {
        if (descriptors.Current() is not { Unit.Length: > 0 } descriptor)
            return;

        try
        {
            var psi = new ProcessStartInfo(Journalctl)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(descriptor.Unit);
            psi.ArgumentList.Add("--no-pager");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("json");

            Process? proc = Process.Start(psi);
            if (proc is null)
            {
                logger.LogWarning("could not start a journal follow for {Unit}", descriptor.Unit);
                return;
            }

            _follow = proc;
            _stop = new CancellationTokenSource();
            _ = Task.Run(() => Pump(proc, descriptor.Id, _stop.Token));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "could not follow the journal for {Unit}", descriptor.Unit);
        }
    }

    /// <summary>Called under the lock.</summary>
    private void Stop()
    {
        try { _stop?.Cancel(); } catch { /* already gone */ }
        try { if (_follow is { HasExited: false }) _follow.Kill(entireProcessTree: true); }
        catch { /* it ended on its own */ }

        _follow?.Dispose();
        _follow = null;
        _stop?.Dispose();
        _stop = null;
    }

    private void Pump(Process proc, string source, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && proc.StandardOutput.ReadLine() is { } raw)
            {
                if (ComponentJournal.Parse(raw, source) is not { } line)
                    continue;

                lock (_gate)
                {
                    foreach (Channel<LogLine> c in _subscribers)
                        c.Writer.TryWrite(line);
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "the journal follow ended");
        }

        // The follow ended on its own — journalctl exited, or the journal became unreadable. Every
        // watcher is completed rather than left waiting on a stream that will never carry anything, so
        // each one closes its own connection and the panel says the tail stopped.
        lock (_gate)
        {
            foreach (Channel<LogLine> c in _subscribers)
                c.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (Channel<LogLine> c in _subscribers)
                c.Writer.TryComplete();
            _subscribers.Clear();
            Stop();
        }
    }

    private sealed class Leaver(ComponentJournalFollower owner, Channel<LogLine> channel) : IDisposable
    {
        private int _left;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _left, 1) == 0)
                owner.Leave(channel);
        }
    }
}
