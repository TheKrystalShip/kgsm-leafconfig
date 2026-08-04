# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
