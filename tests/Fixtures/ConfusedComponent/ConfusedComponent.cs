using TheKrystalShip.KGSM.ComponentConfig;

// A component claiming to be both. It is one or the other — a leaf is run by one node and described
// on that node's disk, an anchor serves the whole cluster — so the generator refuses rather than
// picking one by a precedence rule nobody would remember.

[assembly: Leaf(
    id: "confused",
    displayName: "Confused",
    unit: "kgsm-confused.service",
    role: "A fixture that declares both identities.")]

[assembly: Anchor(
    id: "confused",
    displayName: "Confused",
    unit: "kgsm-confused.service",
    role: "A fixture that declares both identities.")]

[assembly: ConfigFloorSource("appsettings", "/opt/kgsm-confused/kgsm-confused.settings.json")]
