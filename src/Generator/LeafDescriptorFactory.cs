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
        IReadOnlyList<Assembly> sectionAssemblies = [entry, .. Declared(context, entry, assemblyPath)];

        var docs = new XmlDocs(sectionAssemblies.Select(a => Path.ChangeExtension(a.Location, Names.Docs.Extension)));

        IReadOnlyDictionary<string, string?> settings = SettingsFile.Flatten(settingsPath);
        var scanner = new MetadataScanner(entry, sectionAssemblies, docs, settings);
        Descriptor descriptor = scanner.Scan();

        Validator.Check(descriptor, settings.Keys);

        return new BuildResult(descriptor, scanner.Warnings, docs.Found);
    }

    /// <summary>
    /// The other assemblies this leaf declares its settings sections in.
    /// </summary>
    /// <remarks>
    /// Named explicitly rather than discovered, because leaves share libraries: kgsm-bot compiles
    /// against the assistant's projects, and scanning everything beside the binary would pull a section
    /// annotated over there into the bot's descriptor — keys the bot's settings file never declares,
    /// failing a build in a repo nobody touched. A missing one is an error rather than a silent
    /// omission, since the whole point of naming it is that its knobs must appear.
    /// </remarks>
    private static IEnumerable<Assembly> Declared(MetadataLoadContext context, Assembly entry, string entryPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(entryPath))!;

        foreach (CustomAttributeData attribute in entry.GetCustomAttributesData()
                     .Where(a => a.AttributeType.Name == Names.Attributes.SectionAssembly))
        {
            string name = (string)attribute.ConstructorArguments[0].Value!;
            string path = Path.Combine(directory, name + Names.AssemblyExtension);

            if (!File.Exists(path))
                throw new GenException(
                    $"[assembly: LeafSectionAssembly(\"{name}\")] names an assembly that is not beside the " +
                    $"leaf: {path}. Its sections would be missing from the descriptor.");

            yield return context.LoadFromAssemblyPath(path);
        }
    }

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

        // First path wins, so the leaf's own output shadows the frameworks and one framework version
        // shadows the rest. Feeding two copies of the same assembly to the resolver is fatal — several
        // installed frameworks each carry an mscorlib, and the second one to load throws.
        var assemblies = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string source in new[] { directory }.Concat(SharedFrameworks()))
            foreach (string file in Directory.GetFiles(source, "*.dll"))
                assemblies.TryAdd(Path.GetFileName(file), file);

        return new MetadataLoadContext(new PathAssemblyResolver(assemblies.Values));
    }

    /// <summary>
    /// Every shared framework installed beside the running one.
    /// </summary>
    /// <remarks>
    /// A framework-dependent leaf references assemblies that are not in its output directory —
    /// kgsm-api is an ASP.NET app, so Microsoft.AspNetCore.Mvc.Core lives in the shared framework and
    /// nowhere near the binary. Resolving only the runtime directory leaves those unresolvable, and a
    /// type this tool never looks at is enough to stop the scan.
    /// </remarks>
    private static IEnumerable<string> SharedFrameworks()
    {
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        yield return runtime;

        // .../shared/Microsoft.NETCore.App/<version>/ -> .../shared/
        DirectoryInfo? shared = Directory.GetParent(runtime.TrimEnd(Path.DirectorySeparatorChar))?.Parent;
        if (shared is null || !shared.Exists)
            yield break;

        foreach (DirectoryInfo framework in shared.GetDirectories())
        {
            // Highest version wins; a leaf built against an older one still resolves, because metadata
            // only needs the type to exist.
            DirectoryInfo? newest = framework.GetDirectories()
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .LastOrDefault();

            if (newest is not null && !string.Equals(newest.FullName, runtime.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
                yield return newest.FullName;
        }
    }
}
