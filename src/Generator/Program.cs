namespace TheKrystalShip.KGSM.ComponentConfig.Gen;

/// <summary>
/// Writes a leaf's config descriptor from the leaf's own compiled metadata.
/// </summary>
/// <remarks>
/// The descriptor is what the Control Panel renders a leaf's configuration page from, and writing it
/// by hand beside a settings class it had no mechanical connection to is what let the two drift.
/// Deriving it means they cannot: the environment variable comes from the property's position in its
/// bound section, and the default from the settings file the leaf actually loads.
/// <para>
/// This runs as its own process against the built assembly, and reads it as metadata only. Nothing is
/// loaded for execution, so describing a leaf costs it no reflection and no dependency — which is what
/// keeps the Native-AOT leaves AOT.
/// </para>
/// </remarks>
internal static class Program
{
    private const string ArgAssembly = "--assembly";
    private const string ArgSettings = "--settings";
    private const string ArgOut = "--out";
    private const string ArgCheck = "--check";

    private const int Ok = 0;
    private const int Failed = 1;

    private const string Prefix = "componentdescgen: ";

    private const string Usage = $"""
        usage: componentdescgen {ArgAssembly} <leaf.dll> {ArgSettings} <kgsm-<leaf>.settings.json> {ArgOut} <leaf.json> [{ArgCheck}]

          {ArgAssembly}  the leaf's built assembly, read as metadata (never loaded for execution)
          {ArgSettings}  the leaf's settings file, which supplies each field's coded default
          {ArgOut}       where the descriptor is written
          {ArgCheck}     write nothing; fail if the file on disk is not what this would write
        """;

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (GenException ex)
        {
            Console.Error.WriteLine(Prefix + ex.Message);
            return Failed;
        }
    }

    private static int Run(string[] args)
    {
        string? assemblyPath = Value(args, ArgAssembly);
        string? settingsPath = Value(args, ArgSettings);
        string? outPath = Value(args, ArgOut);
        bool check = args.Contains(ArgCheck);

        if (assemblyPath is null || settingsPath is null || outPath is null)
        {
            Console.Error.WriteLine(Usage);
            return Failed;
        }

        BuildResult result = ComponentDescriptorFactory.Build(assemblyPath, settingsPath);

        CheckDestination(result.Descriptor.Identity, outPath);
        Report(result);

        int worst = Ok;
        foreach ((string path, ComponentKind written) in Destinations(result.Descriptor.Identity, outPath))
        {
            string rendered = Emitter.Render(result.Descriptor, written);
            int code = check ? Compare(path, rendered) : Write(path, rendered, result.Descriptor);
            if (code != Ok)
                worst = code;
        }

        return worst;
    }

    /// <summary>
    /// Every descriptor this component is described by, and what each one describes it as.
    /// </summary>
    /// <remarks>
    /// A component that is one kind is described once. One whose kind its deployment decides is
    /// described twice, beside each other, and the deploy installs the one its standing calls for —
    /// so the second file is derived from the first by swapping the suffix rather than named
    /// separately. Two output paths a build could set independently is two places to spell one id.
    /// </remarks>
    private static IEnumerable<(string Path, ComponentKind Written)> Destinations(
        ComponentIdentity identity, string outPath)
    {
        if (identity.Kind != ComponentKind.Either)
        {
            yield return (outPath, identity.Kind);
            yield break;
        }

        bool anchorNamed = outPath.EndsWith(AnchorSuffix, StringComparison.Ordinal);
        string stem = outPath[..^(anchorNamed ? AnchorSuffix.Length : LeafSuffix.Length)];

        yield return (stem + LeafSuffix, ComponentKind.Leaf);
        yield return (stem + AnchorSuffix, ComponentKind.Anchor);
    }

    /// <summary>
    /// The file's name has to agree with what the component is, because the deploy routes on it: a
    /// <c>.leaf.json</c> lands where the node that runs it is scanned, a <c>.anchor.json</c> does not.
    /// Getting it wrong would put an anchor back on some node's service board, which is a deployment
    /// error with no symptom until somebody reads the board and believes it.
    /// </summary>
    private static void CheckDestination(ComponentIdentity identity, string outPath)
    {
        if (identity.Kind == ComponentKind.Either)
        {
            // Either suffix names the pair, because both files are written and the build only has to
            // say where they go. Anything else has no suffix to swap and would silently write one
            // file with the other's name appended.
            if (outPath.EndsWith(LeafSuffix, StringComparison.Ordinal)
                || outPath.EndsWith(AnchorSuffix, StringComparison.Ordinal))
            {
                return;
            }

            throw new GenException(
                $"{identity.Id} declares [assembly: LeafOrAnchor(...)], so it is described both ways and " +
                $"its descriptors are written to '{LeafSuffix}' and '{AnchorSuffix}' paths beside each " +
                $"other — '{Path.GetFileName(outPath)}' is neither. Name either one; the other is written " +
                "beside it.");
        }

        string expected = identity.Kind == ComponentKind.Anchor ? AnchorSuffix : LeafSuffix;
        if (outPath.EndsWith(expected, StringComparison.Ordinal))
            return;

        string declared = identity.Kind == ComponentKind.Anchor ? "[assembly: Anchor(...)]" : "[assembly: Leaf(...)]";
        throw new GenException(
            $"{identity.Id} declares {declared}, so its descriptor is written to a '{expected}' path — " +
            $"'{Path.GetFileName(outPath)}' is not one. The suffix is what the deploy routes on, and a " +
            "component in the wrong directory is read as the wrong kind of thing.");
    }

    private const string LeafSuffix = ".leaf.json";
    private const string AnchorSuffix = ".anchor.json";

    private static void Report(BuildResult result)
    {
        if (!result.DocumentationFound)
            Console.Error.WriteLine(
                Prefix + "no XML documentation file beside the assembly, so no field could be described " +
                "from a <panel> tag. Set <GenerateDocumentationFile>true</GenerateDocumentationFile>.");

        foreach (string warning in result.Warnings)
            Console.Error.WriteLine(Prefix + warning);

        // A description that fell back to the developer-facing summary still ships, but it is named — a
        // silent fallback is how the panel ends up explaining a knob in terms of the code behind it.
        foreach (FieldDef field in result.Descriptor.Fields.Where(f => f.DescriptionFrom == DescriptionSource.Summary))
            Console.Error.WriteLine(
                Prefix + $"{field.Key} is described by its <summary>, which is written for a developer. " +
                $"Add a <panel> tag to say what changing it does for whoever runs the host.");
    }

    private static int Write(string path, string rendered, Descriptor descriptor)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        // Rewriting an identical file would touch its timestamp for nothing, and this runs on every build.
        if (File.Exists(path) && File.ReadAllText(path) == rendered)
        {
            Console.WriteLine(Prefix + $"{descriptor.Identity.Id} unchanged ({descriptor.Fields.Count} fields)");
            return Ok;
        }

        File.WriteAllText(path, rendered);
        Console.WriteLine(Prefix + $"wrote {path} ({descriptor.Fields.Count} fields)");
        return Ok;
    }

    private static int Compare(string path, string rendered)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine(Prefix + $"{path} does not exist. Build the leaf to generate it.");
            return Failed;
        }

        string actual = File.ReadAllText(path);
        if (actual == rendered)
            return Ok;

        Console.Error.WriteLine(
            Prefix + $"{path} is not what this leaf declares.\n{FirstDifference(actual, rendered)}\n" +
            $"Rebuild to regenerate it, and commit the result.");

        return Failed;
    }

    /// <summary>Names the first line that differs, so a stale file says what changed rather than that it did.</summary>
    private static string FirstDifference(string actual, string expected)
    {
        string[] a = actual.Split('\n');
        string[] b = expected.Split('\n');

        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string left = i < a.Length ? a[i] : "(end of file)";
            string right = i < b.Length ? b[i] : "(end of file)";

            if (left != right)
                return $"  line {i + 1}\n    on disk:  {left.Trim()}\n    declared: {right.Trim()}";
        }

        return "  the files differ only in trailing whitespace";
    }

    private static string? Value(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
