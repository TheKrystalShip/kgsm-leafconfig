using TheKrystalShip.KGSM.ComponentConfig;

// A component whose kind its deployment decides: a leaf on a machine that stands alone, an anchor in
// a cluster, from one build. It is described both ways from this one declaration, and the two files
// differ only where the sentence about what it does names a scope.

[assembly: LeafOrAnchor(
    id: "sample-either",
    displayName: "Sample either",
    unit: "kgsm-sample-either.service",
    role: "A fixture serving the host it runs on.",
    AnchorRole = "A fixture serving the whole cluster.",
    ActionNamespace = "either")]

// Declared on the assembly: an action with no single handler, and one acting only on the caller's own
// records. Neither changes with the standing; the standard surface's scope does.
[assembly: Action("either:history.personal-fields", "See who did what", DeclaredEffect.Read, DeclaredScope.Cluster)]
[assembly: Action("preferences.write", "Change own preferences", DeclaredEffect.Write, DeclaredScope.Cluster, Self = true)]

[assembly: ConfigGroup("general", "General", 1)]
[assembly: ConfigFloorSource("appsettings", "/opt/kgsm-sample-either/kgsm-sample-either.settings.json")]

[assembly: ConfigFrameworkField("logLevel", "Logging__LogLevel__Default", "Log level",
    Description = "Minimum severity this component logs.",
    Group = "general",
    Type = ConfigType.Enum,
    Values = ["Debug", "Information"])]

[assembly: ConfigFrameworkNamespace("Logging__", "per-category filtering is open-ended")]
