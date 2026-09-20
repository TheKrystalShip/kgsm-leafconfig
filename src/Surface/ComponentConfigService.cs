using System.Globalization;
using Microsoft.Extensions.Logging;
using TheKrystalShip.Api.Contracts;

namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>What an apply did, and whether the component is bouncing to pick it up.</summary>
/// <param name="Restarting">False means the change is written and is <b>not</b> in force — a different
/// state from one being applied, and one a person has to be told they are in.</param>
public sealed record ComponentApplyOutcome(ComponentConfigApplyResult Result, bool Restarting);

/// <summary>
/// Reads and changes a component's own configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Checking happens before anything is written.</b> Every value is validated against the descriptor's
/// own type, enum and bounds — the same declarations the component's parser clamps to, generated from
/// the same settings class — so the panel cannot accept a value the component would refuse or silently
/// move. A half-applied change would leave it running on a set nobody asked for.
/// </para>
/// <para>
/// <b>There is no canary here, and its absence is the design.</b> When a node's API changes a leaf it
/// restarts a process it is not and can watch it come back; a component changing itself cannot, because
/// the process that would poll is the one being restarted. So the file to remove is logged before the
/// restart is asked for — that log line is the recovery path, written while there is still something
/// running to write it.
/// </para>
/// </remarks>
public sealed class ComponentConfigService(
    ComponentDescriptorStore descriptors,
    ComponentOverrideStore overrides,
    ComponentFloorReader floors,
    ComponentUnitControl unit,
    ILogger<ComponentConfigService> logger)
{
    /// <summary>The current surface, or null when this host installed no descriptor for this component.</summary>
    public ComponentConfigView? Read()
    {
        ComponentDescriptor? descriptor = descriptors.Current();
        if (descriptor is null)
            return null;

        IReadOnlyDictionary<string, string> over = overrides.Read();
        IReadOnlyDictionary<string, string> floor = floors.Read(descriptor.FloorSources);

        var fields = new List<ComponentConfigField>(descriptor.Fields.Count);
        foreach (ComponentFieldDef f in descriptor.Fields)
        {
            over.TryGetValue(f.Env, out string? overridden);
            floor.TryGetValue(f.Env, out string? floored);

            string source =
                overridden is not null ? ComponentConfigSource.Override
                : floored is not null ? ComponentConfigSource.Floor
                : f.Default is not null ? ComponentConfigSource.Default
                : ComponentConfigSource.Unknown;

            // A secret is never echoed, whichever tier it came from. That a value is SET is reported;
            // what it is, is not — and a last-4 fingerprint is offered only for one written here, since
            // that is the only one this surface has in hand.
            bool secret = f.IsSecret;
            string? effective = overridden ?? floored ?? f.Default;

            fields.Add(new ComponentConfigField(
                Key: f.Key,
                EnvName: f.Env,
                Label: f.Label,
                Description: f.Description,
                Type: f.Type,
                Enum: f.Values,
                IsSecret: secret,
                Overridden: overridden is not null,
                Value: secret ? null : overridden,
                Default: secret ? null : f.Default,
                Set: secret ? effective is { Length: > 0 } : null,
                Fingerprint: secret && overridden is { Length: >= 4 } ? overridden[^4..] : null,
                Floor: secret ? null : floored,
                Effective: secret || source == ComponentConfigSource.Unknown ? null : effective,
                Source: source,
                Group: f.Group,
                Risk: f.Risk,
                Unit: f.Unit,
                Min: f.Min,
                Max: f.Max,
                PairedApiKey: f.PairedApiKey,
                DependsOn: f.DependsOn));
        }

        // A component may declare its own surface read-only — the panel API does, because applying a
        // change there means restarting the process serving the request. Its own words are carried,
        // rather than a sentence restated here that would drift from them.
        (bool declaredReadOnly, string? declaredReason) = descriptors.ReadOnlyDeclaration();
        bool restartable = unit.CanRestart(descriptor.Unit, out string? why);
        bool editable = restartable && !declaredReadOnly;

        return new ComponentConfigView(
            Id: descriptor.Id,
            DisplayName: descriptor.DisplayName,
            Unit: descriptor.Unit,
            Fields: fields,
            Groups: [.. descriptor.Groups.Select(g => new ComponentConfigGroup(g.Id, g.Label, g.Order))],
            Editable: editable,
            EditableReason: editable ? null : declaredReadOnly ? declaredReason : why,
            ApplyMode: descriptor.ApplyMode,
            FromDescriptor: true);
    }

    /// <summary>
    /// Apply a change: validate, write, then queue the restart so it happens after the answer has left.
    /// Returns null when this component has no descriptor, or an error string naming the first thing
    /// wrong with the request.
    /// </summary>
    public (ComponentApplyOutcome? Outcome, string? Error) Apply(ComponentConfigUpdate update)
    {
        ComponentDescriptor? descriptor = descriptors.Current();
        if (descriptor is null)
            return (null, null);

        if (Read() is { Editable: false, EditableReason: var locked })
            return (null, locked ?? "This component's configuration cannot be changed on this host.");

        IReadOnlyList<string> reset = update.Reset ?? [];
        IReadOnlyDictionary<string, string> values =
            update.Values ?? new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string key in reset)
        {
            if (values.ContainsKey(key))
                return (null, $"'{key}' is in both values and reset, and those ask for opposite things");
        }

        foreach ((string key, string value) in values)
        {
            if (descriptor.Field(key) is not { } field)
                return (null, $"'{key}' is not a key this component declares");

            if (Invalid(field, value) is { } why)
                return (null, why);
        }

        foreach (string key in reset)
        {
            if (descriptor.Field(key) is null)
                return (null, $"'{key}' is not a key this component declares");
        }

        var rows = new Dictionary<string, string>(overrides.Read(), StringComparer.Ordinal);
        var before = new Dictionary<string, string>(rows, StringComparer.Ordinal);

        foreach ((string key, string value) in values)
            rows[descriptor.Field(key)!.Env] = value;

        foreach (string key in reset)
            rows.Remove(descriptor.Field(key)!.Env);

        if (Same(before, rows))
        {
            // Nothing to write, so nothing to bounce. Restarting to apply a change that is not one would
            // interrupt everything this component is serving for no reason at all.
            ComponentConfigView? unchanged = Read();
            return unchanged is null
                ? (null, null)
                : (new ComponentApplyOutcome(
                    new ComponentConfigApplyResult(
                        ComponentConfigOutcome.Unchanged,
                        new ComponentConfigHealth(CapabilityStatus.Operational, null),
                        "Those values are already in force, so nothing was written.",
                        unchanged),
                    Restarting: false), null);
        }

        overrides.Write(rows);

        // Written before the restart is asked for, because after it there is nobody here to write it:
        // this names the file to remove if the component does not come back.
        logger.LogWarning(
            "configuration changed ({Count} override(s)) — restarting. If {Unit} does not come back, " +
            "remove {Path} and start it again.", rows.Count, descriptor.Unit, overrides.Path);

        ComponentConfigView? after = Read();
        if (after is null)
            return (null, null);

        bool restarting = unit.ScheduleRestart(descriptor.Unit);
        return (new ComponentApplyOutcome(
            new ComponentConfigApplyResult(
                ComponentConfigOutcome.Applied,
                // Unknown, and honestly so: the process that would measure its own health after the
                // restart is the one being restarted.
                new ComponentConfigHealth(CapabilityStatus.Unknown, restarting
                    ? "Restarting to pick the change up."
                    : "Written, but the restart was refused — the change is not in force."),
                restarting
                    ? "Applied. This component is restarting to pick it up."
                    : "Written to the override file, but systemd refused the restart, so it is not in force.",
                after),
            restarting), null);
    }

    /// <summary>
    /// Why a value is not acceptable, or null when it is. The checks are the descriptor's own
    /// declarations, which are generated from the settings type — so what the panel refuses and what the
    /// component would refuse are one statement, made once.
    /// </summary>
    private static string? Invalid(ComponentFieldDef field, string value) => field.Type switch
    {
        "int" when !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            => $"'{field.Key}' takes a whole number",

        "int" => Bounds(field, value),

        "bool" when !IsBool(value)
            => $"'{field.Key}' takes true or false",

        "enum" when field.Values is { Count: > 0 } allowed
                    && !allowed.Contains(value, StringComparer.Ordinal)
            => $"'{field.Key}' takes one of: {string.Join(", ", allowed)}",

        // A path is a string the component resolves; whether it exists is not this surface's claim to
        // make, since a directory can be created between the check and the restart.
        _ => null,
    };

    private static string? Bounds(ComponentFieldDef field, string value)
    {
        double n = double.Parse(value, CultureInfo.InvariantCulture);
        if (field.Min is { } min && n < min)
            return $"'{field.Key}' is at least {min.ToString(CultureInfo.InvariantCulture)}";
        if (field.Max is { } max && n > max)
            return $"'{field.Key}' is at most {max.ToString(CultureInfo.InvariantCulture)}";
        return null;
    }

    private static bool IsBool(string value) => bool.TryParse(value, out _) || value is "1" or "0";

    private static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach ((string k, string v) in a)
        {
            if (!b.TryGetValue(k, out string? other) || !string.Equals(v, other, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
