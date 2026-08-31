using System.Reflection;

namespace TheKrystalShip.KGSM.ComponentConfig.Tests;

/// <summary>
/// Paths to the fixture leaf, handed in by the build rather than guessed at from the test binary's
/// location — a guess would silently pick up a stale copy from another configuration.
/// </summary>
internal static class Fixture
{
    public static string Assembly => Metadata("SampleLeafAssembly");

    public static string Settings => Metadata("SampleLeafSettings");

    /// <summary>A leaf declaring a section assembly it does not ship.</summary>
    public static string MissingSectionAssembly => Metadata("MissingSectionAssembly");

    /// <summary>An ANCHOR, described with the same body a leaf uses.</summary>
    public static string AnchorAssembly => Metadata("SampleAnchorAssembly");

    /// <inheritdoc cref="AnchorAssembly"/>
    public static string AnchorSettings => Metadata("SampleAnchorSettings");

    /// <summary>A component declaring both identities, which is one too many.</summary>
    public static string ConfusedAssembly => Metadata("ConfusedComponentAssembly");

    public static string Golden(string name) => Path.Combine(Metadata("GoldenDirectory"), name);

    private static string Metadata(string key) =>
        typeof(Fixture).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == key)
            .Value!;
}
