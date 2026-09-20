# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — a component reports its own unit (ComponentSurface 1.0.0-dev.3)

`ComponentUnitReader` answers what systemd says about the component's own unit, as the
`ComponentService` row every Services surface renders — state, sub-state, enablement, when it last
became active, its main pid and the memory charged to that process's own cgroup. A node's API reads
this for each of its leaves; a component with no node above it reads its own.

Memory is the main process's cgroup rather than systemd's `MemoryCurrent`, which is the unit
subtree's total and charges a supervised workload to its supervisor. `not-found` and `masked` override
the active state, because a unit that is not installed is not a unit that is stopped.

### Changed — an apply that cannot restart says so (ComponentSurface 1.0.0-dev.2)

An apply whose restart systemd refuses reports `ComponentConfigOutcome.WrittenNotApplied` rather than
`Applied`. The change is on disk and is not in force, which is the opposite claim about what is
running, and a person reading "applied" would stop looking. Takes `Api.Contracts 1.0.0-dev.12` for
the word.

### Added — a component serves its own surface (ComponentSurface 1.0.0-dev.1)

`TheKrystalShip.KGSM.ComponentSurface`, a second package from this repo: the descriptor's **reader**,
beside the generator that writes it. It reads a component's own descriptor, projects it with the
host's deploy floors and the overrides in force, validates and writes a change, reads its unit's
journal and restarts it.

Here rather than anywhere else because the format's schema had one writer and independent readers,
and nothing failed when they disagreed. `SurfaceRoundTripTests` emits from the compiled `SampleLeaf`
fixture and parses the bytes it produced, so a key added to one half and not the other fails a test
instead of reaching a panel as a control that does nothing.

A separate package because `TheKrystalShip.KGSM.ComponentConfig` ships no assembly by construction —
`IncludeBuildOutput=false`, `SuppressDependenciesWhenPacking`, `DevelopmentDependency` — and giving it
one would put a runtime library into every AOT component that wants only the attributes.

Transport-free: the journal follow hands out a channel of lines and frames nothing, so a leaf serves
this over the socket it already has and an anchor over HTTP, and neither difference reaches the
library. AOT-compatible and trimmable, with descriptor reading through a source-generated context.

### Added — a component whose kind its deployment decides

`[LeafOrAnchor]`, a third identity attribute beside `[Leaf]` and `[Anchor]`, for a component that is a
leaf on a machine standing alone and an anchor in a cluster from one build. It carries the same keys
the other two do, plus `AnchorRole` — the role sentence the anchor descriptor states, because a leaf's
usually names the host it serves and an anchor has no host.

A component declaring it is described **both ways**. The build names one output path and the generator
writes the other beside it by swapping the suffix, so the deploy holds both and installs whichever its
standing calls for; two paths a build set independently would be two places to spell one id. The two
files are identical but for that one sentence, and neither states a kind — where a descriptor is
installed remains the only record of what a component is.

Carrying two of the three identities is refused, and the message names which two are present.

## [3.0.0]

### Changed — the package describes COMPONENTS, and a component is a leaf or an anchor

`TheKrystalShip.KGSM.ComponentConfig`, namespace `TheKrystalShip.KGSM.ComponentConfig`, tool
`componentdescgen`. The old name described half of what this does: an anchor has settings, groups,
floor sources and an apply mode exactly as a leaf does, and calling all of it "leaf" was the reason an
anchor's descriptor was indistinguishable from a leaf's.

Two identity attributes and one shared body. `[Leaf]` is a component one node runs, described on that
node's disk and administered as one of its services. `[Anchor]` serves one capability to the whole
cluster, is a peer of every node in it, and is reached by address rather than through a neighbour.
Every other attribute loses its `Leaf` prefix for `Config` — they describe either kind, so naming them
after one was a misnomer. Declaring both identities is refused: a component is one thing, and picking
one by a precedence rule would put it in the wrong directory on a deploy nobody re-read.

**The kind is not written into the descriptor.** The generator routes on the output file's suffix —
`.leaf.json` or `.anchor.json` — and refuses one that disagrees with the identity attribute, so a
component cannot reach the wrong directory without failing the build. Where the file is installed is
what says which; a key repeating it is a second record of one fact that can disagree with the first.

MSBuild properties follow: `ComponentSettingsFile`, `ComponentDescriptorFile`,
`GenerateComponentDescriptor`.

### Added — `[ConfigGpuBackend]`, for GPU spent through a unit the component does not own

An assembly-level attribute naming a systemd unit whose GPU usage is attributed to the declaring
component, emitted as the descriptor's optional `gpuBackendUnits` array. The assistant drives
`llama-server` over HTTP and holds no GPU context of its own, so nothing in its cgroup can reveal
where its GPU work is spent; a component whose own processes hold the context needs no attribute,
since that falls out of the cgroup already.

The key is omitted for one that declares none — an empty array would read as reaching a card and
spending nothing on it. Repeating a unit fails the build, because its memory would be counted twice.

### Changed — package license metadata is GPL-3.0-or-later

`PackageLicenseExpression` matches the repo's own `LICENSE`.

## [2.2.0]

### Fixed
- **A framework-dependent leaf no longer crashes the scan.** kgsm-api is an ASP.NET app, so
  `Microsoft.AspNetCore.Mvc.Core` lives in the shared framework and nowhere near its binary; resolving
  only the runtime directory left it unresolvable, and reading the name of one attribute from it was
  enough to abort. Every shared framework installed beside the running one is now on the resolver's
  path, and an attribute whose type cannot be resolved is skipped rather than fatal — it cannot be a
  Leaf attribute, since those are compiled into the leaf itself.

## [2.1.0]

### Added
- **`[ConfigFrameworkField(SettingsKey = "...")]`** for a setting whose override variable is spelled
  differently from its configuration key. ASP.NET's bind address is the case: the host reads `Urls`
  from configuration and `ASPNETCORE_URLS` from the environment, one setting reached two ways. The
  variable is what an override file must write; the settings key is where the value comes from and
  what coverage checks against.

### Fixed
- **A JSON array flattens to indexed keys**, the way `IConfiguration` addresses one, instead of being
  read as a single opaque value. An **empty** array now declares no key at all — which is correct,
  and load-bearing: an empty allow-list had been reported as an undescribed knob, so a leaf could not
  ship one without failing its own build.

## [2.0.0]

### Changed — a leaf names its section assemblies instead of the generator guessing
- **`[assembly: ConfigSectionAssembly("Name")]` replaces the directory scan.** Discovering sections by
  looking at whatever sits beside the binary is unsafe in this ecosystem, because leaves share
  libraries: kgsm-bot compiles against the assistant's projects, so annotating a settings type over
  there would have pulled it into the bot's descriptor — describing keys the bot's settings file
  never declares, and failing a build in a repo nobody touched. A leaf that names its own assemblies
  cannot be surprised by one it merely depends on.
- **A declared assembly that is not there is an error**, not a silent omission. Skipping it would
  drop every knob it declares, which is precisely the failure this mechanism exists to prevent.

### Migrating
A leaf whose settings types all live in its entry assembly needs no change. One with a settings
library adds a single `[assembly: ConfigSectionAssembly("<its name>")]` beside its `[Leaf]`.

## [1.4.0]

### Added
- **`[ConfigFrameworkField(NoDefault = true)]`**, matching the property attribute. A blank in the
  settings file that the leaf resolves to something else at runtime — a conversation database that
  derives a path under the user's home — is not a default of empty string, and publishing it as one
  is a fabricated value on the screen whose whole job is saying where a value came from.

## [1.3.0]

### Fixed
- **A layered leaf's referenced assemblies are actually found.** The generator ran against the
  intermediate assembly, and a leaf's referenced libraries are only ever copied to the output
  directory — so the sibling scan added in 1.1.0 had nothing to find. It runs against `$(TargetPath)`
  now.
- **A name-keyed map no longer has to be described.** The scan skips a collection because no
  environment variable could deliver it, but its keys are still in the settings file, and coverage
  demanded a description for each one — a knob that cannot exist. The skipped prefixes are carried
  through to the coverage check, so the same rule that drops the field drops its keys.

## [1.2.0]

### Added
- **`[LeafFrameworkField]` takes the same presentation a `[LeafField]` does** — `Min`, `Max`,
  `PairedApiKey`, `DependsOn`. A framework field is a field in every way except having a property to
  hang the attribute on, and a bound that could not be declared meant the Control Panel accepted
  values the leaf would reject.

### Changed
- The attribute is documented for its second use: a section bound from a type **another package**
  owns. kgsm-bot and the assistant both bind `Ollama` and `LlmAgent` and describe those keys
  differently, for different audiences, so the prose belongs with each surface rather than on the
  shared type.

## [1.1.0]

### Added
- **Bound sections may live outside the leaf's entry assembly.** The generator scans every assembly
  beside it that declares a `[LeafSection]`, so a layered leaf — kgsm-bot binds `Discord` and `KGSM`
  from an infrastructure library, kgsm-api the same shape — describes its surface the way a flat one
  does. A descriptor that stopped at the entry assembly silently omitted those sections, and every
  omission is a knob the Control Panel cannot show. Only assemblies actually declaring a section take
  part, and they are ordered by name after the entry assembly so the emitted field order is stable.
- Documentation files from every scanned assembly are merged, so a `<panel>` tag is read wherever the
  property it documents was compiled.

## [1.0.0]

### Added
- **`TheKrystalShip.KGSM.ComponentConfig`** — attributes for declaring a leaf's Control Panel
  configuration surface on its typed settings class, and a build-time generator that writes the leaf
  config descriptor from them.
- **Build-only packaging.** The attributes ship as source and the generator as a tool; the package
  declares no dependencies, so `System.Reflection.MetadataLoadContext` cannot reach a consuming
  leaf's output. A Native-AOT leaf gains nothing at runtime from being described.
- **Descriptions come from a `<panel>` doc tag**, falling back to `<summary>` with a build message
  naming the field.
- **Validation runs before the file is written**, so a descriptor that would misreport a leaf is
  never produced. It covers the settings file in both directions, the field vocabulary, group and
  `dependsOn` references, enum values and defaults, numeric bounds, and floor-source order.
