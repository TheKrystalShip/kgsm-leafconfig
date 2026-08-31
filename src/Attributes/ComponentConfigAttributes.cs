// A component's config descriptor, declared where the configuration itself is declared.
//
// A KGSM component is a LEAF or an ANCHOR, and the difference is what it belongs to: a leaf is run
// by one node and described on that node's disk, an anchor serves one capability to the whole
// cluster and is a peer of every node in it. So there are two identity attributes and one shared
// body — every group, floor source and field below describes either kind, because what a component
// can be configured with is the same question whichever it is.
//
// These attributes are compiled into the component's assembly and read back out of its metadata by
// the generator, which writes the descriptor its deploy installs. Nothing here is read at runtime:
// the component never reflects over itself, so an AOT one carries these as inert metadata and ILC
// discards them. That is why this is a shared *source* file rather than a package reference —
// there is no assembly to trim, and the generator matches attributes by full type name, so each
// component compiling its own copy costs nothing.
//
// Format and rules: tks/leaf-config-descriptor.md.

namespace TheKrystalShip.KGSM.ComponentConfig;

/// <summary>
/// Identifies the assembly as a KGSM <b>leaf</b>: a component one node runs locally, described on
/// that node's disk and administered as one of its services.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class LeafAttribute(string id, string displayName, string unit, string role) : Attribute
{
    /// <summary>Stable leaf id, lowercase kebab. Becomes the descriptor filename stem.</summary>
    public string Id { get; } = id;

    /// <summary>Human name for the panel.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>The systemd unit the API restarts to apply a change.</summary>
    public string Unit { get; } = unit;

    /// <summary>One sentence: what this leaf does.</summary>
    public string Role { get; } = role;

    /// <summary>True for a leaf that idle-exits, so the panel does not read "inactive" as a fault.</summary>
    public bool OnDemand { get; set; }

    /// <summary><c>restart</c> or <c>reload</c>.</summary>
    public string ApplyMode { get; set; } = "restart";

    /// <summary>True for a leaf whose configuration is readable but not editable from the panel.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Why, in the leaf's own words. Required when <see cref="ReadOnly"/>.</summary>
    public string? ReadOnlyReason { get; set; }
}

/// <summary>
/// Identifies the assembly as a KGSM <b>anchor</b>: a component that serves one capability to the
/// whole cluster and is a peer of every node in it.
/// </summary>
/// <remarks>
/// <para>
/// The distinction is not decoration. A leaf is reached through the node that runs it, so its
/// descriptor is installed where that node's API scans. An anchor is reached by ADDRESS, from a
/// browser that is usually nowhere near the machine it runs on, so nothing about it is discovered
/// through a neighbour — sharing a machine with a node is a deployment coincidence, and the
/// descriptor it installs records what runs there rather than offering itself to be administered
/// from next door.
/// </para>
/// <para>
/// Everything below this — groups, floor sources, sections, fields — applies to both. What a
/// component can be configured with is the same question whichever kind it is.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class AnchorAttribute(string id, string displayName, string unit, string role) : Attribute
{
    /// <summary>Stable anchor id, lowercase kebab. Becomes the descriptor filename stem.</summary>
    public string Id { get; } = id;

    /// <summary>Human name for the panel.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>The systemd unit that carries it.</summary>
    public string Unit { get; } = unit;

    /// <summary>One sentence: what this anchor holds for the cluster.</summary>
    public string Role { get; } = role;

    /// <summary>True for an anchor that idle-exits, so a reader does not take "inactive" for a fault.</summary>
    public bool OnDemand { get; set; }

    /// <summary><c>restart</c> or <c>reload</c>.</summary>
    public string ApplyMode { get; set; } = "restart";

    /// <summary>True for an anchor whose configuration is readable but not editable from the panel.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Why, in the anchor's own words. Required when <see cref="ReadOnly"/>.</summary>
    public string? ReadOnlyReason { get; set; }
}

/// <summary>
/// One section of the panel. Fields land in a group by naming its id; the order given here is the
/// order the groups render in, and it is also the order fields are emitted in.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class ConfigGroupAttribute(string id, string label, int order) : Attribute
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public int Order { get; } = order;
}

/// <summary>
/// Where this leaf's own configuration comes from, <b>lowest precedence first</b> — the same order
/// the leaf itself resolves them, so the settings file is declared first.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class ConfigFloorSourceAttribute(string kind, string path) : Attribute
{
    /// <summary><c>appsettings</c>, <c>systemd-unit</c> or <c>env-file</c>.</summary>
    public string Kind { get; } = kind;

    public string Path { get; } = path;
}

/// <summary>
/// A systemd unit whose GPU usage belongs to this leaf — a model backend the leaf drives without
/// owning the process that spends the card.
/// </summary>
/// <remarks>
/// Declared rather than discovered, because discovery cannot see it: a leaf's own GPU contexts fall
/// out of its cgroup, and this covers the case where they do not exist. The figures the monitor
/// attributes this way stay a separate block carrying the unit they came from, so a surface reads
/// <i>"GPU via kgsm-llama-chat.service"</i> rather than folding a backend's memory into the leaf's
/// own resource numbers.
/// <para>
/// A unit that is masked, absent or not running contributes nothing and is not a fault — the
/// declaration says where this leaf's GPU work would be spent, not that it is being spent now.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class ConfigGpuBackendAttribute(string unit) : Attribute
{
    /// <summary>The systemd unit name, suffix included: <c>kgsm-llama-chat.service</c>.</summary>
    public string Unit { get; } = unit;
}

/// <summary>
/// Names another assembly this leaf's settings sections live in — an infrastructure library, a
/// configuration layer below the host that binds it.
/// </summary>
/// <remarks>
/// Opt-in rather than "scan whatever is beside the binary", because leaves share libraries. kgsm-bot
/// compiles against the assistant's projects, so a section annotated over there would otherwise
/// appear in the bot's descriptor — describing keys the bot's settings file never declares, and
/// breaking a build in a repo nobody touched. A leaf naming its own assemblies cannot be surprised
/// by one it merely depends on.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class ConfigSectionAssemblyAttribute(string name) : Attribute
{
    /// <summary>Simple assembly name, without the extension.</summary>
    public string Name { get; } = name;
}

/// <summary>
/// Marks a bound settings type and names the configuration section it binds from. Every
/// <see cref="ConfigFieldAttribute"/> property under it is addressed as <c>Section__Property</c>,
/// which is exactly how <c>IConfiguration</c> maps an environment variable onto it — so a
/// descriptor cannot name a variable the leaf does not read.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class ConfigSectionAttribute(string section) : Attribute
{
    public string Section { get; } = section;
}

/// <summary>The wire type the panel renders a field as. <c>Auto</c> derives it from the property.</summary>
internal enum ConfigType
{
    Auto,
    String,
    Int,
    Bool,
    Enum,
    Secret,
    Path,
    Csv,
    Duration,
    Float,
}

/// <summary>How the panel presents an edit. Never blocks one.</summary>
internal enum ConfigRisk
{
    /// <summary>The failure mode is the leaf doing its job differently.</summary>
    Safe,

    /// <summary>Changing it can sever the link between this leaf and something else.</summary>
    Wiring,

    /// <summary>Changing it can drop data.</summary>
    Destructive,
}

/// <summary>
/// Declares one configurable knob. The <c>env</c> name and the coded default are derived — the
/// first from the property's position in the section, the second from the settings file — so this
/// carries only what cannot be derived: the stable key, the operator-facing label, and the
/// presentation.
/// </summary>
/// <remarks>
/// The field's <c>description</c> comes from a <c>&lt;panel&gt;</c> tag in the property's XML doc
/// comment, falling back to <c>&lt;summary&gt;</c>. Operator prose and developer prose answer
/// different questions, which is why they get separate tags rather than one shared sentence.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class ConfigFieldAttribute(string key, string label) : Attribute
{
    /// <summary>No bound value. <see cref="Min"/>/<see cref="Max"/> default to this and are omitted.</summary>
    public const int NoBound = int.MinValue;

    /// <summary>
    /// The stable wire id, camelCase. <b>Immutable once shipped</b> — stored overrides are keyed by
    /// it, so renaming one orphans a live override and silently reverts the leaf to its floor. It is
    /// spelled out rather than derived from the property name for exactly that reason: a property
    /// rename must not be able to move it.
    /// </summary>
    public string Key { get; } = key;

    /// <summary>Short human name. No units here; use <see cref="Unit"/>.</summary>
    public string Label { get; } = label;

    /// <summary>A <see cref="ConfigGroupAttribute"/> id. Unset renders under <i>General</i>.</summary>
    public string? Group { get; set; }

    /// <summary>Wire type. <see cref="ConfigType.Auto"/> reads it off the property's CLR type.</summary>
    public ConfigType Type { get; set; } = ConfigType.Auto;

    /// <summary>Allowed values for an enum field. Derived automatically from a C# enum property.</summary>
    public string[]? Values { get; set; }

    /// <summary>Lower bound. Mirror the parser's own floor by pointing both at one constant.</summary>
    public int Min { get; set; } = NoBound;

    /// <summary>Upper bound.</summary>
    public int Max { get; set; } = NoBound;

    /// <summary>Display suffix: <c>ms</c>, <c>s</c>, <c>days</c>, <c>MB</c>, <c>%</c>.</summary>
    public string? Unit { get; set; }

    public ConfigRisk Risk { get; set; } = ConfigRisk.Safe;

    /// <summary>A kgsm-api config key that must move in lockstep with this one.</summary>
    public string? PairedApiKey { get; set; }

    /// <summary>Another field's key. This one has no effect unless that one is set.</summary>
    public string? DependsOn { get; set; }

    /// <summary>
    /// Suppresses the derived default. Set it when the settings file's value is not what the leaf
    /// actually falls back to — a blank that resolves to the machine name at runtime is not a
    /// default of empty string, and publishing it as one would be a fabricated value.
    /// </summary>
    public bool NoDefault { get; set; }
}

/// <summary>
/// Excludes a bound property from the descriptor. Collections and name-keyed maps are skipped
/// automatically (one variable cannot express a collection, and systemd refuses a variable name
/// containing a hyphen, so a map keyed by an instance name is undeliverable through the env file);
/// this is for the rest.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class ConfigIgnoreAttribute : Attribute;

/// <summary>
/// A namespace of configuration keys this leaf honours but does not describe, because the set is
/// open-ended by construction — per-category log filtering can spell any category name there is.
/// </summary>
/// <remarks>
/// Every other key in the settings file must be described or the build fails. This is the one
/// escape hatch, and it is deliberately a declaration rather than a rule buried in a tool: an
/// exemption that has to be written down, with a reason, is one someone has to justify.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class ConfigFrameworkNamespaceAttribute(string prefix, string reason) : Attribute
{
    /// <summary>Key prefix, spelled the way the settings file flattens: <c>Logging__</c>.</summary>
    public string Prefix { get; } = prefix;

    /// <summary>Why this namespace cannot be enumerated.</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// A configurable key the leaf honours without a settings property of its own to hang an attribute
/// on — the ecosystem logging level, a host-builder variable, or a section bound from a type another
/// package owns.
/// </summary>
/// <remarks>
/// That last case is why the prose is declared here rather than on the shared type: kgsm-bot and the
/// assistant both bind <c>Ollama</c> and <c>LlmAgent</c>, and each describes those keys for its own
/// surface — "what the bot says when it hits the step limit" is not what the assistant says. Prose
/// written for one surface has to live with that surface.
/// <para>
/// Everything else about the field still holds: the key is immutable, the environment variable must
/// be one the leaf genuinely reads, and the settings file remains the source of the default.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class ConfigFrameworkFieldAttribute(string key, string env, string label) : Attribute
{
    public string Key { get; } = key;
    public string Env { get; } = env;
    public string Label { get; } = label;

    /// <summary>Operator-facing prose. No property means no XML doc to fall back on.</summary>
    public string Description { get; set; } = string.Empty;

    public string? Group { get; set; }
    public ConfigType Type { get; set; } = ConfigType.String;
    public string[]? Values { get; set; }

    /// <summary>
    /// The coded default, for a key the settings file does not declare. When the file does declare it,
    /// the file wins — it is the artifact the leaf actually loads.
    /// </summary>
    public string? Default { get; set; }

    /// <summary>
    /// The settings-file key this variable overrides, when the two are spelled differently. ASP.NET's
    /// bind address is the case that needs it: the host reads <c>Urls</c> from configuration and
    /// <c>ASPNETCORE_URLS</c> from the environment, and they are one setting reached two ways. The
    /// variable is what an override file has to write; this is where the value comes from and what
    /// coverage checks against.
    /// </summary>
    public string? SettingsKey { get; set; }

    /// <summary>Lower bound. See <see cref="ConfigFieldAttribute.Min"/>.</summary>
    public int Min { get; set; } = ConfigFieldAttribute.NoBound;

    /// <summary>Upper bound.</summary>
    public int Max { get; set; } = ConfigFieldAttribute.NoBound;

    public string? Unit { get; set; }
    public ConfigRisk Risk { get; set; } = ConfigRisk.Safe;

    /// <summary>A kgsm-api config key that must move in lockstep with this one.</summary>
    public string? PairedApiKey { get; set; }

    /// <summary>Another field's key. This one has no effect unless that one is set.</summary>
    public string? DependsOn { get; set; }

    /// <summary>
    /// Suppresses the derived default, for the same reason <see cref="ConfigFieldAttribute.NoDefault"/>
    /// does: a blank in the settings file that the leaf resolves to something else at runtime is not a
    /// default of empty string, and publishing it as one would be a fabricated value.
    /// </summary>
    public bool NoDefault { get; set; }
}
