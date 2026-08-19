using TheKrystalShip.KGSM.LeafConfig;

// A leaf that exists only to be scanned. Every shape the generator has to handle appears here once:
// a bounded number, a secret, a C# enum, a suppressed default, a nested section, a name-keyed map, an
// explicitly ignored property, and a bound property nobody described.

[assembly: Leaf(
    id: "sample",
    displayName: "Sample",
    unit: "kgsm-sample.service",
    role: "A fixture leaf, used to pin the generator's output.")]

// The Retry section lives in SampleLibrary, a layer below this assembly.
[assembly: LeafSectionAssembly("SampleLibrary")]

[assembly: LeafGroup("general", "General", 1)]
[assembly: LeafGroup("net", "Networking", 2)]

// A backend the fixture drives without owning: its GPU is attributed here, labelled with the unit.
[assembly: LeafGpuBackend("kgsm-sample-backend.service")]

[assembly: LeafFloorSource("appsettings", "/opt/kgsm-sample/kgsm-sample.settings.json")]
[assembly: LeafFloorSource("env-file", "/etc/kgsm-sample/kgsm-sample.env")]

[assembly: LeafFrameworkNamespace("Logging__",
    "per-category filtering is open-ended: any category name is a valid key")]

[assembly: LeafFrameworkField("logLevel", "Logging__LogLevel__Default", "Log level",
    Description = "Minimum severity this leaf logs.",
    Group = "general",
    Type = LeafType.Enum,
    Values = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"])]

namespace SampleLeaf;

/// <summary>The packet-filtering implementation the fixture pretends to drive.</summary>
public enum FirewallBackend
{
    Ufw,
    NfTables,
}

/// <summary>The fixture leaf's bound configuration.</summary>
[LeafSection("Sample")]
public sealed class SampleSettings
{
    /// <summary>Listening port. Nullable so a blank value binds as unset rather than throwing.</summary>
    /// <panel>TCP port the sample leaf listens on.</panel>
    [LeafField("port", "Port", Group = "net", Min = 1, Max = 65535)]
    public int? Port { get; set; }

    /// <summary>Shared secret for the fixture's imaginary API. Written, never read back.</summary>
    [LeafField("token", "API token", Group = "general", Type = LeafType.Secret)]
    public string? Token { get; set; }

    /// <summary>Which backend to drive.</summary>
    /// <panel>Packet-filtering implementation this leaf drives. Changing it rewrites nothing that is
    /// already applied.</panel>
    [LeafField("backend", "Firewall backend", Group = "net")]
    public FirewallBackend? Backend { get; set; }

    /// <summary>Identity this host reports under. Blank resolves to the machine name.</summary>
    /// <panel>Identity this host reports under. Defaults to the machine's hostname.</panel>
    [LeafField("hostId", "Host id", Group = "general", Risk = LeafRisk.Wiring,
        PairedApiKey = "Api__HostId", NoDefault = true)]
    public string HostId { get; set; } = string.Empty;

    /// <summary>
    /// Per-instance channel ids. Keyed by a name the operator chose, so it can contain a hyphen —
    /// which systemd refuses in a variable name. The generator skips it for that reason: no
    /// environment variable could ever deliver it.
    /// </summary>
    public Dictionary<string, string> Channels { get; set; } = [];

    /// <summary>Not configuration: a computed cache path the leaf owns.</summary>
    [LeafIgnore]
    public string CachePath { get; set; } = string.Empty;

    /// <summary>Bound, but described by nothing. The generator warns rather than inventing prose.</summary>
    public string? Undescribed { get; set; }

    /// <summary>A nested section, addressed the way the binder addresses it.</summary>
    public StatusSettings Status { get; set; } = new();
}

/// <summary>Presence markers, nested one level under <see cref="SampleSettings"/>.</summary>
public sealed class StatusSettings
{
    /// <summary>Marker shown for a running server.</summary>
    /// <panel>Marker shown beside a server that is running.</panel>
    [LeafField("statusOnline", "Online marker", Group = "general")]
    public string Online { get; set; } = "online";
}
