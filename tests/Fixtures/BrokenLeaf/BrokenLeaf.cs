using TheKrystalShip.KGSM.ComponentConfig;

// A leaf that names a section assembly which is not there — a typo, or a project reference someone
// removed. The generator has to refuse rather than quietly emit a descriptor missing those knobs.

[assembly: Leaf(
    id: "broken",
    displayName: "Broken",
    unit: "kgsm-broken.service",
    role: "A fixture leaf that names a section assembly it does not ship.")]

[assembly: ConfigSectionAssembly("NotShipped")]
[assembly: ConfigFloorSource("appsettings", "/opt/kgsm-broken/kgsm-broken.settings.json")]
