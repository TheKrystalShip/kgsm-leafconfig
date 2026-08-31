# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — `Anchor`, for a component that is a cluster member rather than a node's leaf

An optional flag on `[Leaf]`, emitted as the descriptor's `anchor` key. An auth anchor serves one
capability to the whole cluster and is a peer of the node it happens to sit beside, so it belongs on
no node's service board — kgsm-api reads this key to leave it off, and the component is reached as
the member it is. It still ships a descriptor, because what it can be configured with is worth
describing wherever that is read.

Omitted for a leaf that is not one, the same way `readOnly` is: absence is what a node's leaf looks
like, and every descriptor but an anchor's is one. A new optional leaf-level key is additive within
`schemaVersion: 1`, so the version is unchanged and every existing reader ignores it.

### Added — `[LeafGpuBackend]`, for GPU spent through a unit the leaf does not own

An assembly-level attribute naming a systemd unit whose GPU usage is attributed to the declaring
leaf, emitted as the descriptor's optional `gpuBackendUnits` array. The assistant drives
`llama-server` over HTTP and holds no GPU context of its own, so nothing in its cgroup can reveal
where its GPU work is spent; a leaf whose own processes hold the context needs no attribute, since
that falls out of the cgroup already.

The key is omitted for a leaf that declares none — an empty array would read as a leaf that reaches a
card and spends nothing on it. Repeating a unit fails the build, because its memory would be counted
twice. A new optional leaf-level key is additive within `schemaVersion: 1`, so the version is
unchanged and every existing reader ignores it.

### Changed — package license metadata is GPL-3.0-or-later

`PackageLicenseExpression` now matches the repo's own `LICENSE`, which it had never declared. Already
published versions keep the metadata they were built with, since a published version is immutable —
the correction reaches consumers on the next version bump.

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
- **`[LeafFrameworkField(SettingsKey = "...")]`** for a setting whose override variable is spelled
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
- **`[assembly: LeafSectionAssembly("Name")]` replaces the directory scan.** Discovering sections by
  looking at whatever sits beside the binary is unsafe in this ecosystem, because leaves share
  libraries: kgsm-bot compiles against the assistant's projects, so annotating a settings type over
  there would have pulled it into the bot's descriptor — describing keys the bot's settings file
  never declares, and failing a build in a repo nobody touched. A leaf that names its own assemblies
  cannot be surprised by one it merely depends on.
- **A declared assembly that is not there is an error**, not a silent omission. Skipping it would
  drop every knob it declares, which is precisely the failure this mechanism exists to prevent.

### Migrating
A leaf whose settings types all live in its entry assembly needs no change. One with a settings
library adds a single `[assembly: LeafSectionAssembly("<its name>")]` beside its `[Leaf]`.

## [1.4.0]

### Added
- **`[LeafFrameworkField(NoDefault = true)]`**, matching the property attribute. A blank in the
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
- **`TheKrystalShip.KGSM.LeafConfig`** — attributes for declaring a leaf's Control Panel
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
