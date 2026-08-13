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

**Every .NET leaf in the ecosystem uses it** — `kgsm-monitor`, `kgsm-watchdog`, `kgsm-scheduler`,
`kgsm-firewall`, `kgsm-bot`, `kgsm-llm` and `kgsm-api`, 215 fields between them. It is the only
implementation of the descriptor's rules; none of those repos carries a descriptor test of its own.

## Why generated

A descriptor written by hand sits in a different directory from the settings class it describes, with
no mechanical connection to it, and has to be updated by whoever adds a knob. It drifted. Generating
it removes both the step and the failure it existed to prevent:

- **`env` is derived** from the property's position under its bound section, so the descriptor cannot
  name a variable the leaf does not read — the format's worst failure mode, an override the panel
  reports as applied that changes nothing.
- **`default` is read from the leaf's settings file**, the same artifact it loads at runtime, so the
  panel cannot publish a value the leaf never starts with.
- **`type` is read off the property**, and a C# enum supplies its own `values`.
- **Bounds can point at the constant the parser clamps against**, so the panel cannot accept a value
  the leaf would silently move.

What cannot be derived stays declared, beside the property: the stable key, the label, the group, the
risk, and the prose.

## It does not make a leaf reflect

The attributes are compiled in **as source**, and the generator reads them back out of the built
assembly through `MetadataLoadContext` — metadata only, in its own process, with nothing loaded for
execution. Three things follow, and each is load-bearing rather than incidental:

- There is no assembly to load, trim or resolve, and the generator matches attributes **by name**
  rather than by type identity — which is what lets every leaf compile its own copy and still be read
  by one tool.
- The generator cannot run leaf code, and needs no runtime the leaf targets.
- The package declares **no dependencies at all**, so nothing it needs can reach a leaf's output.

Measured, not assumed, on the four Native-AOT leaves: the AOT publish stays at zero ILC warnings,
`System.Reflection.MetadataLoadContext.dll` never appears in a publish directory, and none of the
descriptor's strings survive into a native binary.

## Using it

```xml
<PackageReference Include="TheKrystalShip.KGSM.LeafConfig" Version="2.2.0" PrivateAssets="all" />

<PropertyGroup>
  <LeafSettingsFile>kgsm-monitor.settings.json</LeafSettingsFile>
  <LeafDescriptorFile>$(MSBuildProjectDirectory)/../../deploy/kgsm-monitor.leaf.json</LeafDescriptorFile>
</PropertyGroup>
```

The package turns on `GenerateDocumentationFile` (that is how `<panel>` reaches the generator),
includes the attribute source, and adds an `AfterTargets="Build"` step that rewrites the descriptor.
`GenerateLeafDescriptor=false` uses the attributes without generating anything — which is what a
settings *library* sets, leaving generation to the host that names it.

**Edit the settings class, not the JSON** — the build overwrites it. Commit what the build produces.

Each leaf's `deploy.sh` publishes before it installs the descriptor, so what lands in
`/var/lib/kgsm/leaves/` is regenerated from the source being deployed rather than from whatever was
last committed.

## What fails the build

The generator validates before it writes, so a descriptor that would misreport a leaf is never
produced:

- a settings key no `[LeafField]` describes, or a described key the settings file does not declare
- a field with no description, an unknown `group` or `dependsOn`, a duplicate key
- an enum with no values, or a default outside them; bounds on a non-numeric field
- an unknown `type`, `risk` or `applyMode`; `readOnly` with no reason
- `floorSources` that does not start with the settings file
- a named section assembly that is not beside the leaf

A field described only by its `<summary>`, and a bound property nothing describes, both still build —
and the build names each one, so neither stays invisible.

## Vocabulary

| Attribute | Where | What it declares |
|---|---|---|
| `[Leaf]` | assembly | id, display name, unit, role, `onDemand`, `applyMode`, `readOnly` |
| `[LeafGroup]` | assembly | a panel section and its order |
| `[LeafFloorSource]` | assembly | where the leaf's own config comes from, lowest precedence first |
| `[LeafSectionAssembly]` | assembly | another assembly this leaf's settings sections live in |
| `[LeafFrameworkField]` | assembly | a key the leaf honours with no settings property of its own |
| `[LeafFrameworkNamespace]` | assembly | a key prefix that cannot be enumerated, and why |
| `[LeafSection]` | class | the configuration section a settings type binds from |
| `[LeafField]` | property | key, label, group, type, bounds, unit, risk, `pairedApiKey`, `dependsOn` |
| `[LeafIgnore]` | property | bound, but not configuration |

### What a descriptor cannot express

Collections and name-keyed maps are skipped automatically, along with their keys: one variable cannot
express a collection, and systemd refuses a variable name containing a hyphen, so a map keyed by an
instance name could never be delivered through the env file at all. A leaf declares such a surface in
its settings file and it stays off the panel.

A key namespace that cannot be enumerated — per-category log filtering can spell any category name
there is — needs an explicit `[LeafFrameworkNamespace]` **with a reason**. It is a declaration in the
leaf's source rather than a rule inside this tool, so an exemption has to be justified where someone
will read it.

### Layered and shared configuration

**A leaf whose settings types live in a library names that assembly**, rather than the generator
scanning whatever sits beside the binary. Leaves here share libraries — `kgsm-bot` compiles against
the assistant's projects — so a section annotated in one repo must not be able to appear in another's
descriptor. A named assembly that is missing is an error, never a silent omission.

**A section bound from a type another package owns** is declared with `[LeafFrameworkField]` instead.
`kgsm-bot` and the assistant both bind `Ollama` and `LlmAgent`, and each describes those keys for its
own audience — what the bot says when it runs out of tool steps is not what the assistant says — so
the prose lives with the surface that shows it rather than on the shared type.

`[LeafFrameworkField(SettingsKey = "…")]` covers a setting whose override variable is spelled
differently from its configuration key: ASP.NET reads `Urls` from configuration and `ASPNETCORE_URLS`
from the environment, one setting reached two ways.

## Build

```bash
dotnet build kgsm-leafconfig.slnx
dotnet test kgsm-leafconfig.slnx                          # generator + validator + settings flattening
../scripts/publish-packages.sh kgsm-leafconfig     # pack + push to the org's feed
```

The tests run the generator against `tests/Fixtures/SampleLeaf`, a real compiled leaf covering every
shape the scanner handles — nested section, name-keyed map, C# enum, secret, suppressed default,
ignored property, a section in a declared library — and pin the whole emitted document against a
golden copy.

Consumers resolve the package from the local feed by `id+version`, and NuGet caches on that pair, so
**bump `<Version>` in `src/Package/Package.csproj` for any change**: a same-version repack serves the
old package to every leaf, from the cache, with no error. Keep all leaves on one version — the point
of the pin is that one engine describes every leaf.

The tool can also be run directly, which is how a build failure is best read:

```bash
dotnet leafdescgen.dll --assembly <leaf.dll> --settings <kgsm-leaf.settings.json> --out <leaf.json>
dotnet leafdescgen.dll ... --check       # write nothing; fail if the file on disk is stale
```

## License

This project is licensed under the GPL-3.0 license. See the [LICENSE](LICENSE) file for details.
