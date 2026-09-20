using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// Reads a component's descriptor from disk, cached briefly so a page view costs one read rather than
/// one per field.
/// </summary>
/// <remarks>
/// The deploy rewrites the file, so the cache expiring is how a redeploy's new surface arrives without
/// a restart. A descriptor that is absent, unreadable, or written to a schema this build does not know
/// leaves the component reporting that it has no configuration surface — which is a different answer
/// from a surface with no fields, and never a shape invented to fill the gap.
/// </remarks>
public sealed class ComponentDescriptorStore(ComponentSurfaceOptions options, ILogger<ComponentDescriptorStore> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private ComponentDescriptor? _cached;
    private DateTime _readUtc = DateTime.MinValue;
    private string? _reported;

    /// <summary>The descriptor, or null when this host installed none for this component.</summary>
    public ComponentDescriptor? Current()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _readUtc < Ttl)
                return _cached;

            _cached = Load();
            _readUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    /// <summary>Drop the cache so the next read hits the disk. For tests and for an explicit refresh.</summary>
    public void Invalidate()
    {
        lock (_gate)
            _readUtc = DateTime.MinValue;
    }

    private ComponentDescriptor? Load()
    {
        string path = options.DescriptorPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Miss($"no config descriptor at {path} — this component serves no configuration surface");

        RawComponentDescriptor? raw;
        try
        {
            using FileStream stream = File.OpenRead(path);
            raw = JsonSerializer.Deserialize(stream, ComponentDescriptorJson.Default.RawComponentDescriptor);
        }
        catch (Exception ex)
        {
            return Miss($"the config descriptor at {path} is not readable: {ex.Message}");
        }

        if (raw is null)
            return Miss($"the config descriptor at {path} is empty");

        if (raw.SchemaVersion != ComponentDescriptor.SupportedSchemaVersion)
            return Miss($"the config descriptor at {path} declares schemaVersion {raw.SchemaVersion}, " +
                        $"and this build reads {ComponentDescriptor.SupportedSchemaVersion}");

        if (raw.Id is not { Length: > 0 } id || raw.Unit is not { Length: > 0 } unit)
            return Miss($"the config descriptor at {path} is missing its id or unit");

        var fields = new List<ComponentFieldDef>(raw.Fields?.Count ?? 0);
        foreach (RawComponentField f in raw.Fields ?? [])
        {
            // A field with no key or no variable to bind through cannot be shown or written, so it is
            // skipped rather than rendered as a control that would silently do nothing.
            if (f.Key is not { Length: > 0 } key || f.Env is not { Length: > 0 } env)
                continue;

            fields.Add(new ComponentFieldDef(
                key, env, f.Label ?? key, f.Description ?? "", f.Group,
                f.Type ?? "string", f.Default, f.Values, f.Min, f.Max, f.Unit, f.Risk ?? "safe",
                f.PairedApiKey, f.DependsOn));
        }

        List<ComponentGroupDef> groups =
        [
            .. (raw.Groups ?? [])
                .Where(g => g.Id is { Length: > 0 })
                .Select(g => new ComponentGroupDef(g.Id!, g.Label ?? g.Id!, g.Order))
                .OrderBy(g => g.Order),
        ];

        List<ComponentFloorSource> floors =
        [
            .. (raw.FloorSources ?? [])
                .Where(f => f.Kind is { Length: > 0 } && f.Path is { Length: > 0 })
                .Select(f => new ComponentFloorSource(f.Kind!, f.Path!)),
        ];

        _reported = null;
        return new ComponentDescriptor(
            id,
            raw.DisplayName ?? id,
            unit,
            raw.Role ?? "",
            raw.ApplyMode ?? "restart",
            raw.OnDemand,
            floors,
            groups,
            fields);
    }

    /// <summary>
    /// Whether the descriptor declares itself read-only, and why. Separate from <see cref="Current"/>
    /// because it is a statement about applying rather than about the surface: the values are still
    /// worth reading, and the component that publishes them says so itself.
    /// </summary>
    public (bool ReadOnly, string? Reason) ReadOnlyDeclaration()
    {
        string path = options.DescriptorPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return (false, null);

        try
        {
            using FileStream stream = File.OpenRead(path);
            RawComponentDescriptor? raw =
                JsonSerializer.Deserialize(stream, ComponentDescriptorJson.Default.RawComponentDescriptor);
            return raw is null ? (false, null) : (raw.ReadOnly, raw.ReadOnlyReason);
        }
        catch
        {
            // An unreadable descriptor has already been reported by the load above; a component is not
            // declared read-only on the strength of a file nobody could read.
            return (false, null);
        }
    }

    /// <summary>Reported once per reason rather than on every read, since this is polled.</summary>
    private ComponentDescriptor? Miss(string why)
    {
        if (!string.Equals(_reported, why, StringComparison.Ordinal))
        {
            logger.LogWarning("{Why}", why);
            _reported = why;
        }
        return null;
    }
}
