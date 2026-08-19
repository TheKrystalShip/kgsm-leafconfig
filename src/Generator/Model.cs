namespace TheKrystalShip.KGSM.LeafConfig.Gen;

/// <summary>The descriptor's leaf-level keys, read from the assembly's <c>[Leaf]</c> attribute.</summary>
internal sealed record LeafIdentity(
    string Id,
    string DisplayName,
    string Unit,
    string Role,
    bool OnDemand,
    string ApplyMode,
    bool ReadOnly,
    string? ReadOnlyReason);

internal sealed record GroupDef(string Id, string Label, int Order);

internal sealed record FloorSource(string Kind, string Path);

/// <summary>
/// One emitted field. <see cref="Env"/> is derived from the property's path through the bound
/// sections and <see cref="Default"/> from the settings file, so neither can disagree with what the
/// leaf actually reads.
/// </summary>
internal sealed record FieldDef
{
    public required string Key { get; init; }
    public required string Env { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public string? Group { get; init; }
    public required string Type { get; init; }
    public IReadOnlyList<string>? Values { get; init; }
    public string? Default { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
    public string? Unit { get; init; }
    public required string Risk { get; init; }
    public string? PairedApiKey { get; init; }
    public string? DependsOn { get; init; }

    /// <summary>
    /// The settings-file key this field's value comes from, when it is spelled differently from
    /// <see cref="Env"/>. Coverage checks against this rather than the variable name.
    /// </summary>
    public string? SettingsKey { get; init; }

    /// <summary>Where the description came from, so the tool can report a fallback rather than hide it.</summary>
    public required DescriptionSource DescriptionFrom { get; init; }

    /// <summary>Declaration order within the group: framework fields first, then bound properties.</summary>
    public required int Order { get; init; }
}

internal enum DescriptionSource
{
    /// <summary>A <c>&lt;panel&gt;</c> tag — prose written for an operator.</summary>
    Panel,

    /// <summary>The <c>&lt;summary&gt;</c> tag — developer prose, standing in until someone writes better.</summary>
    Summary,

    /// <summary>Declared inline on a framework field, which has no property to document.</summary>
    Declared,

    /// <summary>Nothing to say. This is an error, not a blank.</summary>
    Missing,
}

/// <summary>A key namespace the leaf honours but cannot enumerate, and why.</summary>
internal sealed record FrameworkNamespace(string Prefix, string Reason);

internal sealed record Descriptor(
    LeafIdentity Identity,

    /// <summary>
    /// Systemd units whose GPU usage is attributed to this leaf. Empty for a leaf that drives no
    /// backend, and the key is then absent from the file rather than written as an empty array —
    /// absence is what every reader already treats as "this leaf reaches no card".
    /// </summary>
    IReadOnlyList<string> GpuBackendUnits,
    IReadOnlyList<FloorSource> FloorSources,
    IReadOnlyList<GroupDef> Groups,
    IReadOnlyList<FieldDef> Fields,
    IReadOnlyList<FrameworkNamespace> FrameworkNamespaces,

    /// <summary>
    /// Env-key prefixes of bound properties the scan skipped because no variable could deliver them —
    /// a name-keyed map or a list. Their keys are in the settings file and will never be fields, so
    /// coverage has to know about them or it would demand a description for something undescribable.
    /// </summary>
    IReadOnlyList<string> UndeliverablePrefixes);
