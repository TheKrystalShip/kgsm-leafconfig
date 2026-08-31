namespace TheKrystalShip.KGSM.ComponentConfig.Gen;

/// <summary>
/// Which kind of component this assembly is. A leaf is run by one node and described on that node's
/// disk; an anchor serves one capability to the whole cluster and is a peer of every node in it;
/// either means the kind is decided by the deployment, so both descriptors are written and the deploy
/// installs the one its standing calls for. The kind is not written into the descriptor — where the
/// file is installed is what says which, and two records of one fact can disagree.
/// </summary>
internal enum ComponentKind
{
    Leaf,
    Anchor,
    Either,
}

/// <summary>The descriptor's component-level keys, read from whichever identity attribute the assembly
/// carries. All three carry the same keys — what a component can be configured with is the same
/// question whichever kind it is.</summary>
/// <param name="AnchorRole">
/// What <see cref="Role"/> says when the descriptor being written is the anchor one. Only an
/// <see cref="ComponentKind.Either"/> component has both, and only because a leaf's sentence usually
/// names the host it serves, which an anchor does not have.
/// </param>
internal sealed record ComponentIdentity(
    ComponentKind Kind,
    string Id,
    string DisplayName,
    string Unit,
    string Role,
    bool OnDemand,
    string ApplyMode,
    bool ReadOnly,
    string? ReadOnlyReason,
    string? AnchorRole = null)
{
    /// <summary>The role sentence for the descriptor being written, which is the only field whose
    /// value depends on which of the two an either-kind component is being described as.</summary>
    public string RoleFor(ComponentKind written) =>
        written == ComponentKind.Anchor && !string.IsNullOrWhiteSpace(AnchorRole) ? AnchorRole! : Role;
}

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
    ComponentIdentity Identity,

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
