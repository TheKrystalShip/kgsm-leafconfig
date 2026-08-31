using TheKrystalShip.KGSM.ComponentConfig;

// An anchor, described with the same body a leaf uses. That is the point of the fixture: the two
// identity attributes differ in nothing but which they are, so an anchor's groups, floor sources and
// fields go through exactly the code a leaf's do.

[assembly: Anchor(
    id: "sample-anchor",
    displayName: "Sample anchor",
    unit: "kgsm-sample-anchor.service",
    role: "A fixture anchor holding one capability for the whole cluster.")]

[assembly: ConfigGroup("general", "General", 1)]
[assembly: ConfigFloorSource("appsettings", "/opt/kgsm-sample-anchor/kgsm-sample-anchor.settings.json")]

[assembly: ConfigFrameworkField("logLevel", "Logging__LogLevel__Default", "Log level",
    Description = "Minimum severity this anchor logs.",
    Group = "general",
    Type = ConfigType.Enum,
    Values = ["Debug", "Information"])]

[assembly: ConfigFrameworkNamespace("Logging__", "per-category filtering is open-ended")]
