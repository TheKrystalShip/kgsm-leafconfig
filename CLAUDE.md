# CLAUDE.md — kgsm-leafconfig

Guidance for Claude Code working in **kgsm-leafconfig**. The umbrella `../CLAUDE.md` owns the
cross-cutting ecosystem rules; this file covers what is specific to this package.

## What this is

`TheKrystalShip.KGSM.LeafConfig` — the attributes a leaf declares its Control Panel configuration
surface with, plus the build-time generator that writes `deploy/<leaf>.leaf.json` from them.
`README.md` is the usage reference and the attribute vocabulary; read it first.

This is **not a leaf**. It deploys nowhere, has no `deploy/` directory and no systemd unit. It is a
build-time dependency of leaves, consumed as a versioned `PackageReference` from the org's GitHub
Packages feed the same way `kgsm-lib` is.

**Authority for the descriptor *format* is `../leaf-config-descriptor.md`.** This repo implements a
producer for it; when the two disagree, that document wins and this code is the bug.

## Commands

```bash
dotnet build kgsm-leafconfig.slnx
dotnet test kgsm-leafconfig.slnx
../scripts/publish-packages.sh kgsm-leafconfig     # pack + push to the org's feed
```

A consumer resolves the package by `id+version` and NuGet caches on that pair, so **bump
`<Version>` in `src/Package/Package.csproj` for any change** — a same-version repack serves the old
package to every leaf, from the cache, with no error.

## Layout

| Path | What |
|---|---|
| `src/Attributes/` | The attribute definitions. Plain source, no project — packed into `build/src/` and compiled into each leaf by the package's props file. |
| `src/Generator/` | The tool. A plain JIT console app (`leafdescgen`), packed into `tools/net10.0/`. |
| `src/Package/` | Packaging only. Produces the nupkg; builds nothing of its own. |
| `build/*.props` `*.targets` | What a consuming leaf gets: the attribute source, `GenerateDocumentationFile`, and the `AfterTargets="Build"` generation step. |
| `tests/Fixtures/SampleLeaf/` | A real compiled leaf covering every shape the scanner handles. |
| `tests/LeafConfig.Tests/` | Generator, validator and settings-flattening tests. |

## The invariant this package exists to protect

**A leaf gains nothing at runtime from being described.** Three decisions hold that up, and each one
is load-bearing:

1. **The attributes ship as source, not as an assembly.** There is no reference to resolve, trim or
   load. It also means the generator matches attributes **by name**, not by type identity — which is
   what lets every leaf compile its own copy and still be readable by one tool.
2. **The generator reads metadata, never types.** `MetadataLoadContext` cannot load a type for
   execution, so the tool cannot run leaf code and needs no runtime the leaf targets.
3. **The package declares no dependencies** (`SuppressDependenciesWhenPacking`, `IncludeBuildOutput=false`,
   `DevelopmentDependency`). Without that, `System.Reflection.MetadataLoadContext` becomes a
   transitive runtime dependency of every leaf — an assembly shipped into a Native-AOT publish to
   support a tool that never runs there.

Verified on `kgsm-monitor`: zero ILC warnings, no `System.Reflection.MetadataLoadContext.dll` in the
publish output, and none of the descriptor's strings in the native binary. **If you change the
packaging, re-check all three** — the failure is silent, and it lands in another repo.

## Gotchas

- **Pack collects its file list in `_GetPackageFiles`.** A target that adds `None` items
  `BeforeTargets="GenerateNuspec"` runs too late and its files are dropped with no error — the
  package just ships without the tool. `PackGeneratorTool` hooks
  `TargetsForTfmSpecificContentInPackage` for that reason, and errors if it published nothing.
- **The generator has no apphost** (`UseAppHost=false`). It is always invoked as
  `dotnet leafdescgen.dll`, and the extensionless apphost is read by NuGet as a *directory* when
  packing, burying the real files a level deeper.
- **A nested `<MSBuild>` call inherits global properties.** The leaf-side target that invokes the
  tool must not pass an AOT publish's `RuntimeIdentifier` down to a portable tool build; the package's
  targets sidestep this by invoking the packed dll rather than building anything.
- **Attribute arguments are folded by the compiler**, which is why `Min = SomeClass.SomeConst`
  resolves in the emitted descriptor. That is what lets a bound and the parser's clamp be one
  declaration — do not replace it with anything that reads the constant at runtime.
- **The `id`-vs-installed-filename rule is not checked here.** A repo generates
  `deploy/kgsm-x.leaf.json` and installs it as `/var/lib/kgsm/leaves/x.json`; the rename is
  `deploy-common.sh`'s, and it checks the id there.
- **The golden file pins the whole emitted document.** When a change to the format is intended,
  regenerate it and read the diff — that file is the record of what every leaf's descriptor will
  look like.

## Version tracking

- **Version source:** `<Version>` in `src/Package/Package.csproj`.
- Bump for any change; consumers pin an exact version and NuGet caches by `id+version`.
- Update `CHANGELOG.md` under `## [Unreleased]` for every meaningful change.

## Documentation & comments: present-tense canon only

Prose in this repo — every doc, `README`/`CLAUDE.md` section, and in-code comment — describes
**how the thing works right now**, nothing else. History lives in the `CHANGELOG` and git
history; never duplicate it into docs or code.

- **No transitions.** Never "was X, now Y", "used to…", "changed from…", "no longer…", or any
  before/after framing. State the current rule flat: a sentence that only makes sense to a reader
  who knows what the code *used to* do is dead weight, because that "before" no longer exists
  anywhere in the code.
- **Tombstones leave no marker.** When something is removed — dying naturally as part of the work,
  or explicitly asked to be deleted — the removal is silent: no *"removed X"*, no *"X is gone"*,
  no *"deprecated, use Y instead"* pointing at a corpse. The prose reads as if it never was. Code
  kept while the thing that justified it was deleted gets a live present-tense reason to exist —
  or goes too.
- **No residue of the active work.** References only meaningful *during* a piece of work don't
  survive it: *"temporary shim for the rework"*, *"added to satisfy the new requirement"*,
  milestone/phase labels (*"per M2"*, *"the Phase 1 step"*). If a line's justification is the work
  that produced it rather than the system as it now stands, it goes.
- **No volatile numbers.** Counts and versions that drift — how many projects/files/tests/
  partials exist, a dependency's pinned version, a file's line count — never go in prose: they are
  stale the moment anything changes, and nothing fails to remind anyone. Name the authoritative
  source instead (the csproj, the directory, the barrel file). A number belongs in prose only when
  it *is* the contract (a port, a timeout, a cap) or a measured fact that is itself the reason a
  design exists.
- **Edits are replacements, not appends.** When changing an existing feature, rewrite the affected
  doc/comment fresh as if writing it for the first time — never append a correction under the
  stale version, and never leave the stale version standing beside the new. The current revision
  does not converse with prior revisions.

A reader six months from now should learn the system from the doc without knowing what it
replaced. If you catch yourself explaining a change, stop — that sentence belongs in the commit
message. When touching prose that already violates this, rewrite it to present-tense canon in
passing.
