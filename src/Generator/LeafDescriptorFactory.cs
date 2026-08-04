using System.Reflection;
using System.Runtime.InteropServices;

namespace TheKrystalShip.KGSM.LeafConfig.Gen;

/// <summary>A built descriptor, plus anything the leaf ought to hear about while building it.</summary>
internal sealed record BuildResult(
    Descriptor Descriptor,
    IReadOnlyList<string> Warnings,
    bool DocumentationFound);

/// <summary>
/// Builds a leaf's descriptor from its compiled assembly and its settings file — the whole pipeline
/// in one call, so the command line and the tests take the same path through it.
/// </summary>
internal static class LeafDescriptorFactory
{
    public static BuildResult Build(string assemblyPath, string settingsPath)
    {
        using MetadataLoadContext context = Open(assemblyPath);

        Assembly entry = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        IReadOnlyList<Assembly> sectionAssemblies = [entry, .. Siblings(context, assemblyPath)];

        var docs = new XmlDocs(sectionAssemblies.Select(a => Path.ChangeExtension(a.Location, Names.Docs.Extension)));

        IReadOnlyDictionary<string, string?> settings = SettingsFile.Flatten(settingsPath);
        var scanner = new MetadataScanner(entry, sectionAssemblies, docs, settings);
        Descriptor descriptor = scanner.Scan();

        Validator.Check(descriptor, settings.Keys);

        return new BuildResult(descriptor, scanner.Warnings, docs.Found);
    }

    /// <summary>
    /// The other assemblies beside the entry one that carry bound settings types.
    /// </summary>
    /// <remarks>
    /// A leaf's configuration types do not have to live in its entry assembly — kgsm-bot binds
    /// <c>Discord</c> and <c>KGSM</c> from an infrastructure library its Discord host consumes. Scanning
    /// the whole output directory means a layered leaf describes its surface the same way a flat one
    /// does. Only assemblies that actually declare a <c>[LeafSection]</c> take part, so a leaf's
    /// third-party dependencies contribute nothing; ordering is by assembly name after the entry
    /// assembly, so the emitted field order does not depend on how the directory happens to be read.
    /// </remarks>
    private static IEnumerable<Assembly> Siblings(MetadataLoadContext context, string entryPath)
    {
        string full = Path.GetFullPath(entryPath);
        string directory = Path.GetDirectoryName(full)!;

        foreach (string path in Directory.GetFiles(directory, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
        {
            if (string.Equals(Path.GetFullPath(path), full, StringComparison.Ordinal))
                continue;

            Assembly candidate;
            try
            {
                candidate = context.LoadFromAssemblyPath(path);
                // Native and resource-only files sit in the same directory and carry no types.
                if (!candidate.GetTypes().Any(HasSection))
                    continue;
            }
            catch (Exception)
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static bool HasSection(Type type) =>
        type.GetCustomAttributesData().Any(a => a.AttributeType.Name == Names.Attributes.Section);

    /// <summary>
    /// Resolves the leaf's assembly and everything it references from its own output directory plus
    /// the running framework — enough to read metadata, which is all this needs.
    /// </summary>
    /// <remarks>
    /// <see cref="MetadataLoadContext"/> never loads a type for execution. That is the property the
    /// whole approach rests on: the generator cannot run leaf code, needs no runtime the leaf targets,
    /// and leaves the leaf itself with no reflection of its own.
    /// </remarks>
    private static MetadataLoadContext Open(string assemblyPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (directory is null || !File.Exists(assemblyPath))
            throw new GenException($"the assembly is missing: {assemblyPath}. Build the leaf first.");

        List<string> assemblies =
        [
            .. Directory.GetFiles(directory, "*.dll"),
            .. Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"),
        ];

        return new MetadataLoadContext(new PathAssemblyResolver(assemblies));
    }
}
