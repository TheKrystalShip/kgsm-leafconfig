# kgsm-leafconfig

**`TheKrystalShip.KGSM.LeafConfig`** — a KGSM leaf declares its Control Panel configuration surface
on its typed settings class, and the leaf config descriptor is generated from it at build time.

```csharp
[LeafSection("Monitor")]
public sealed class MonitorSettings
{
    /// <summary>Sampling cadence in milliseconds. Floor 100 — a lower value is raised to it.</summary>
    /// <panel>How often the monitor samples host and per-server metrics.</panel>
    [LeafField("intervalMs", "Sample interval", Group = "sampling", Min = Floors.IntervalMs, Unit = "ms")]
    public int? IntervalMs { get; set; }
}
```

That produces the field's entry in `deploy/kgsm-monitor.leaf.json`, which the leaf's `deploy.sh`
installs into `/var/lib/kgsm/leaves/` for `kgsm-api` to read. The format itself is specified in
`tks/leaf-config-descriptor.md`; this package is how a leaf produces one.

## Why generated

A descriptor written by hand sits in a different directory from the settings class it describes, with
no mechanical connection to it, and has to be updated by whoever adds a knob. It drifted. Generating
it removes both the step and the failure it existed to prevent:

- **`env` is derived** from the property's position under its bound section, so the descriptor cannot
  name a variable the leaf does not read — the format's worst failure mode, an override the panel
  reports as applied that changes nothing.
- **`default` is read from the leaf's settings file**, the same artifact it loads at runtime.
- **`type` is read off the property**, and a C# enum supplies its own `values`.
- **Bounds can point at the constant the parser clamps against**, so the panel cannot accept a value
  the leaf would silently move.

What cannot be derived stays declared, beside the property: the stable key, the label, the group, the
risk, and the prose.

## It does not make a leaf reflect

The attributes are compiled in **as source**, and the generator reads them back out of the built
assembly through `MetadataLoadContext` — metadata only, in its own process, with nothing loaded for
execution. A leaf gains no runtime dependency, no reflection, and no assembly to trim. On
`kgsm-monitor` this is measured rather than assumed: the AOT publish stays at zero ILC warnings, and
none of the descriptor's strings appear in the native binary.

## Using it

```xml
<PackageReference Include="TheKrystalShip.KGSM.LeafConfig" Version="1.0.0" PrivateAssets="all" />

<PropertyGroup>
  <LeafSettingsFile>kgsm-monitor.settings.json</LeafSettingsFile>
  <LeafDescriptorFile>$(MSBuildProjectDirectory)/../../deploy/kgsm-monitor.leaf.json</LeafDescriptorFile>
</PropertyGroup>
```

The package turns on `GenerateDocumentationFile` (that is how `<panel>` reaches the generator),
includes the attribute source, and adds an `AfterTargets="Build"` step that rewrites the descriptor.
`GenerateLeafDescriptor=false` uses the attributes without generating anything.

**Edit the settings class, not the JSON** — the build overwrites it. Commit what the build produces.

## What fails the build

The generator validates before it writes, so a descriptor that would misreport a leaf is never
produced:

- a settings key no `[LeafField]` describes, or a described key the settings file does not declare
- a field with no description, an unknown `group` or `dependsOn`, a duplicate key
- an enum with no values, or a default outside them; bounds on a non-numeric field
- an unknown `type`, `risk` or `applyMode`; `readOnly` with no reason
- `floorSources` that does not start with the settings file

A field described only by its `<summary>` still builds, and the build says which field it was.

## Vocabulary

| Attribute | Where | What it declares |
|---|---|---|
| `[Leaf]` | assembly | id, display name, unit, role, `onDemand`, `applyMode`, `readOnly` |
| `[LeafGroup]` | assembly | a panel section and its order |
| `[LeafFloorSource]` | assembly | where the leaf's own config comes from, lowest precedence first |
| `[LeafFrameworkField]` | assembly | a key the leaf honours with no settings property of its own |
| `[LeafFrameworkNamespace]` | assembly | a key prefix that cannot be enumerated, and why |
| `[LeafSection]` | class | the configuration section a settings type binds from |
| `[LeafField]` | property | key, label, group, type, bounds, unit, risk, `pairedApiKey`, `dependsOn` |
| `[LeafIgnore]` | property | bound, but not configuration |

Collections and name-keyed maps are skipped automatically: one variable cannot express a collection,
and systemd refuses a variable name containing a hyphen, so a map keyed by an instance name could
never be delivered through the env file at all.

## Build

```bash
dotnet build kgsm-leafconfig.slnx
dotnet test kgsm-leafconfig.slnx                          # generator + validator + settings flattening
dotnet pack src/Package/Package.csproj -c Release -o /home/heisen/local-nuget
```

The tests run the generator against `tests/Fixtures/SampleLeaf`, a real compiled leaf covering every
shape the scanner handles, and pin the whole emitted file against a golden copy.
