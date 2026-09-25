using System.Reflection;
using System.Runtime.InteropServices;

namespace TheKrystalShip.KGSM.ComponentConfig.Gen;

/// <summary>
/// A built descriptor and action surface, plus anything the component ought to hear about while
/// building them.
/// </summary>
internal sealed record BuildResult(
    Descriptor Descriptor,
    ActionSurface Actions,
    IReadOnlyList<string> Warnings,
    bool DocumentationFound);

/// <summary>
/// Builds a leaf's descriptor from its compiled assembly and its settings file — the whole pipeline
/// in one call, so the command line and the tests take the same path through it.
/// </summary>
internal static class ComponentDescriptorFactory
{
    public static BuildResult Build(string assemblyPath, string settingsPath)
    {
        using MetadataLoadContext context = Open(assemblyPath);

        Assembly entry = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        IReadOnlyList<Assembly> sectionAssemblies =
            [entry, .. Declared(context, entry, assemblyPath, Names.Attributes.SectionAssembly)];

        var docs = new XmlDocs(sectionAssemblies.Select(a => Path.ChangeExtension(a.Location, Names.Docs.Extension)));

        IReadOnlyDictionary<string, string?> settings = SettingsFile.Flatten(settingsPath);
        var scanner = new MetadataScanner(entry, sectionAssemblies, docs, settings);
        Descriptor descriptor = scanner.Scan();

        Validator.Check(descriptor, settings.Keys);

        IReadOnlyList<Assembly> actionAssemblies =
            [entry, .. Declared(context, entry, assemblyPath, Names.Attributes.ActionAssembly)];
        ActionSurface actions = new ActionScanner(context, actionAssemblies, descriptor.Identity).Scan();

        return new BuildResult(descriptor, actions, scanner.Warnings, docs.Found);
    }

    /// <summary>
    /// The other assemblies this component declares its settings sections, or its actions, in.
    /// </summary>
    /// <remarks>
    /// Named explicitly rather than discovered, because components share libraries: kgsm-bot compiles
    /// against the assistant's projects, and scanning everything beside the binary would pull a section
    /// or an action annotated over there into the bot's — keys the bot's settings file never declares,
    /// actions the bot never performs, failing a build in a repo nobody touched. A missing one is an
    /// error rather than a silent omission, since the whole point of naming it is that its declarations
    /// must appear.
    /// </remarks>
    private static IEnumerable<Assembly> Declared(MetadataLoadContext context, Assembly entry, string entryPath, string attributeName)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(entryPath))!;

        foreach (CustomAttributeData attribute in entry.GetCustomAttributesData()
                     .Where(a => a.AttributeType.Name == attributeName))
        {
            string name = (string)attribute.ConstructorArguments[0].Value!;
            string path = Path.Combine(directory, name + Names.AssemblyExtension);
            string declared = attributeName[..^"Attribute".Length];

            if (!File.Exists(path))
                throw new GenException(
                    $"[assembly: {declared}(\"{name}\")] names an assembly that is not beside the " +
                    $"component: {path}. What it declares would be missing.");

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
