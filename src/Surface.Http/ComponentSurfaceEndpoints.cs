using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using TheKrystalShip.Api.Contracts;

namespace TheKrystalShip.KGSM.ComponentSurface.Http;

/// <summary>
/// What a component answers about <b>itself</b>: its configuration, its unit, its journal and the
/// commands it declares.
/// </summary>
/// <remarks>
/// <para>
/// A component owns all of that wherever it runs. What differs is the way something reaches it — a
/// leaf over the unix socket it already has, with the node's API relaying, an anchor at its own member
/// address — and these are the same routes either way, so one Control Panel page renders both and the
/// relay needs no knowledge of which component it is forwarding to.
/// </para>
/// <para>
/// <b>No gate.</b> A component gates with what it holds: an anchor verifies the cluster's session, a
/// leaf's socket is guarded by its own file permissions and there is nobody else on it. So this maps
/// routes and nothing else, and the caller mounts it under whatever it already gates with.
/// </para>
/// <para>
/// <b>Every response is written through a source-generated context</b> rather than returned as an
/// object for the host to serialize. A Native-AOT component has no reflection to fall back on, and
/// requiring each one to configure matching JSON options would be a second place for the wire format
/// to be spelled.
/// </para>
/// </remarks>
public static class ComponentSurfaceEndpoints
{
    /// <summary>The heartbeat that keeps an idle journal from reading as a dropped connection.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Maps <c>GET/PUT config</c>, <c>GET system</c>, <c>GET logs</c>, <c>GET logs/stream</c> and
    /// <c>GET commands</c> onto <paramref name="builder"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapComponentSurface(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // What this component can be configured with, and what it is running on.
        builder.MapGet("/config", static (HttpContext ctx) =>
        {
            var config = ctx.RequestServices.GetRequiredService<ComponentConfigService>();
            return config.Read() is { } view
                ? Json(ctx, StatusCodes.Status200OK, view, ApiContractsJson.Default.ComponentConfigView)
                : NoDescriptor(ctx, "so it describes no configuration surface");
        });

        // Set or reset keys, then restart to pick them up.
        //
        // The restart is queued BEFORE this answer is written and the answer still arrives, because
        // systemd stops the unit with SIGTERM and the host drains what is already in flight. That
        // ordering is what lets the answer carry whether the job was accepted — a refused restart is a
        // change written and NOT in force, which is a different state from one being applied and a
        // person has to be told which they are in.
        builder.MapPut("/config", static async (HttpContext ctx) =>
        {
            ComponentConfigUpdate? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync(
                    ctx.Request.Body, ApiContractsJson.Default.ComponentConfigUpdate, ctx.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                body = null;
            }

            if (body is null)
            {
                await Refuse(ctx, StatusCodes.Status400BadRequest, "malformed_request",
                    "The request body is not readable.").ConfigureAwait(false);
                return;
            }

            var config = ctx.RequestServices.GetRequiredService<ComponentConfigService>();
            (ComponentApplyOutcome? outcome, string? error) = config.Apply(body);

            if (error is not null)
            {
                await Refuse(ctx, StatusCodes.Status400BadRequest, "invalid_value", error).ConfigureAwait(false);
                return;
            }

            if (outcome is null)
            {
                await NoDescriptor(ctx, "so there is nothing to configure").ConfigureAwait(false);
                return;
            }

            await Json(ctx, StatusCodes.Status200OK, outcome.Result,
                ApiContractsJson.Default.ComponentConfigApplyResult).ConfigureAwait(false);
        });

        // What systemd reports about this component's unit — the row every Services surface renders.
        builder.MapGet("/system", static async (HttpContext ctx) =>
        {
            var units = ctx.RequestServices.GetRequiredService<ComponentUnitReader>();
            if (await units.ReadAsync(ctx.RequestAborted).ConfigureAwait(false) is not { } row)
            {
                await NoDescriptor(ctx, "so it names no unit to report on").ConfigureAwait(false);
                return;
            }

            await Json(ctx, StatusCodes.Status200OK, row, ApiContractsJson.Default.ComponentService)
                .ConfigureAwait(false);
        });

        // The scrollback. A journal that cannot be read is a different fact from one that has nothing
        // in it, and it is reported as one rather than as an empty page.
        //
        // `lines` is read off the query rather than bound as a parameter. Binding one asks the
        // framework to reflect over the delegate's signature, which a Native-AOT component has no way
        // to do — and this library is consumed by several.
        builder.MapGet("/logs", static (HttpContext ctx) =>
        {
            int? lines = int.TryParse(ctx.Request.Query["lines"], out int n) ? n : null;
            var journal = ctx.RequestServices.GetRequiredService<ComponentJournal>();
            return journal.Read(lines) is { } read
                ? Json(ctx, StatusCodes.Status200OK, new LogPage(read, null), ApiContractsJson.Default.LogPage)
                : Refuse(ctx, StatusCodes.Status503ServiceUnavailable, "journal_unreadable",
                    "This component's journal could not be read on this host.");
        });

        builder.MapGet("/logs/stream", StreamAsync);

        // The commands it declares, passed through from its own manifest.
        builder.MapGet("/commands", static (HttpContext ctx) =>
        {
            var manifest = ctx.RequestServices.GetRequiredService<ComponentCommandManifest>();
            return manifest.Read(out string? json, out string? why) switch
            {
                ComponentCommandManifest.ManifestRead.Ok => WriteRaw(ctx, json!),
                ComponentCommandManifest.ManifestRead.None => Refuse(ctx, StatusCodes.Status404NotFound,
                    "no_manifest", "This component declares no commands."),
                _ => Refuse(ctx, StatusCodes.Status503ServiceUnavailable, "manifest_unreadable",
                    why ?? "This component's command manifest could not be read."),
            };
        });

        return builder;
    }

    /// <summary>
    /// <c>GET logs/stream</c> — the same lines, as they happen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Server-sent events rather than a socket, because this carries one thing in one direction and a
    /// browser reconnects it for free.
    /// </para>
    /// <para>
    /// <b>Follow-only.</b> The caller hydrated its scrollback from the read above and applies lines
    /// from the next one on, so nothing here replays history — sending it would show every line twice
    /// on every attach.
    /// </para>
    /// <para>
    /// The comment line at the start is what makes a proxy release the response: a stream that has
    /// carried no bytes is one several of them hold on to until it does.
    /// </para>
    /// </remarks>
    private static async Task StreamAsync(HttpContext ctx)
    {
        var follower = ctx.RequestServices.GetRequiredService<ComponentJournalFollower>();

        if (follower.Watch() is not { } watch)
        {
            await Refuse(ctx, StatusCodes.Status503ServiceUnavailable, "journal_unreadable",
                "This component's journal could not be followed on this host.").ConfigureAwait(false);
            return;
        }

        using IDisposable handle = watch.Handle;

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await ctx.Response.WriteAsync(": open\n\n", ctx.RequestAborted).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);

        var heartbeat = new PeriodicTimer(Heartbeat);
        Task<bool> tick = heartbeat.WaitForNextTickAsync(ctx.RequestAborted).AsTask();

        try
        {
            while (!ctx.RequestAborted.IsCancellationRequested)
            {
                Task<bool> lines = watch.Lines.WaitToReadAsync(ctx.RequestAborted).AsTask();
                Task done = await Task.WhenAny(lines, tick).ConfigureAwait(false);

                if (done == tick)
                {
                    if (!await tick.ConfigureAwait(false))
                        break;
                    tick = heartbeat.WaitForNextTickAsync(ctx.RequestAborted).AsTask();
                    await ctx.Response.WriteAsync(": ping\n\n", ctx.RequestAborted).ConfigureAwait(false);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
                    continue;
                }

                // The follow ended. Closing is the honest answer: the reader says the tail stopped
                // rather than showing a live pill over a stream carrying nothing.
                if (!await lines.ConfigureAwait(false))
                    break;

                while (watch.Lines.TryRead(out LogLine? line))
                {
                    string json = JsonSerializer.Serialize(line, ApiContractsJson.Default.LogLine);
                    await ctx.Response.WriteAsync("data: " + json + "\n\n", ctx.RequestAborted).ConfigureAwait(false);
                }

                await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The reader left. Nothing to report: the handle's disposal is what matters, and it is what
            // stops the follow when this was the last watcher.
        }
        finally
        {
            heartbeat.Dispose();
        }
    }

    private static Task Json<T>(HttpContext ctx, int status, T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(value, type), ctx.RequestAborted);
    }

    // The manifest's own bytes, untouched. Re-serializing it would mean holding its schema here.
    private static Task WriteRaw(HttpContext ctx, string json)
    {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync(json, ctx.RequestAborted);
    }

    private static Task NoDescriptor(HttpContext ctx, string consequence) =>
        Refuse(ctx, StatusCodes.Status404NotFound, "no_descriptor",
            "This component has no config descriptor installed, " + consequence + ".");

    private static Task Refuse(HttpContext ctx, int status, string code, string message) =>
        Json(ctx, status, new ErrorEnvelope(new ErrorBody(code, message)),
            ApiContractsJson.Default.ErrorEnvelope);
}
