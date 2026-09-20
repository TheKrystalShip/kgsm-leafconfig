namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// What a component can be configured with, read back from the descriptor its own build generated.
/// </summary>
/// <remarks>
/// <para>
/// This is the descriptor's <b>reader</b>, and it lives beside the generator that writes the same file
/// so the round trip is one repo's business. The format itself is specified in
/// <c>tks/leaf-config-descriptor.md</c>.
/// </para>
/// <para>
/// A component reads its own descriptor because it serves its own configuration surface: it owns what
/// it can be configured with, what is overridden, and what an override means. Which directory the file
/// sits in says whether the component is a leaf or an anchor; nothing in here needs to know, because
/// the surface is the same either way.
/// </para>
/// </remarks>
public sealed record ComponentDescriptor(
    string Id,
    string DisplayName,
    string Unit,
    string Role,
    string ApplyMode,
    bool OnDemand,
    IReadOnlyList<ComponentFloorSource> FloorSources,
    IReadOnlyList<ComponentGroupDef> Groups,
    IReadOnlyList<ComponentFieldDef> Fields)
{
    /// <summary>The one schema version this reader understands. Another is refused rather than guessed
    /// at — the format's own forward-compatibility contract.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>One field by its stable key, or null when the component declares none by that name.</summary>
    public ComponentFieldDef? Field(string key) =>
        Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
}

/// <summary>A display section on the component's configuration page.</summary>
public sealed record ComponentGroupDef(string Id, string Label, int Order);

/// <summary>Where one tier of a component's configuration comes from, as its descriptor declares it.</summary>
public sealed record ComponentFloorSource(string Kind, string Path);

/// <summary>
/// One settable key. <see cref="Env"/> is the variable an override writes, which is what makes this
/// surface schema-agnostic: it never knows what a value means, only the name it binds through.
/// </summary>
public sealed record ComponentFieldDef(
    string Key,
    string Env,
    string Label,
    string Description,
    string? Group,
    string Type,
    string? Default,
    IReadOnlyList<string>? Values,
    double? Min,
    double? Max,
    string? Unit,
    string Risk,
    string? PairedApiKey,
    string? DependsOn)
{
    /// <summary>A secret is never echoed back, whichever tier its value came from.</summary>
    public bool IsSecret => string.Equals(Type, "secret", StringComparison.Ordinal);
}
