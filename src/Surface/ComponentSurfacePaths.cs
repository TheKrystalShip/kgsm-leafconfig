namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// Where a component's own files are, resolved for the standing it is deployed in.
/// </summary>
/// <remarks>
/// <para>
/// A component is a leaf on one machine and an anchor on another, and its build writes both
/// descriptors. The deploy installs the one its standing calls for and clears the other, so exactly
/// one of the two locations holds a file and <b>which one is the answer to what this component
/// currently is</b>. Nothing here decides that; it reads what the deploy decided.
/// </para>
/// <para>
/// The anchor location is checked first. If both somehow hold a file the deploy did not finish, and a
/// peer of every node is the more consequential of the two readings to get right.
/// </para>
/// <para>
/// A configured path is an operator naming one deliberately and always wins, whether or not it
/// exists: being told where to look and finding nothing is a fact worth reporting, and quietly
/// reading somewhere else would hide it.
/// </para>
/// </remarks>
public static class ComponentSurfacePaths
{
    /// <summary>Where a deploy installs the descriptor of a component that is an anchor here.</summary>
    public const string AnchorDirectory = "/var/lib/kgsm/anchors";

    /// <summary>Where a deploy installs the descriptor of a component that is one of this node's leaves.</summary>
    public const string LeafDirectory = "/var/lib/kgsm/leaves";

    /// <summary>The descriptor this component should read about itself.</summary>
    public static string Descriptor(string componentId, string? configured = null) =>
        Resolve(AnchorDirectory, LeafDirectory, componentId + ".json", configured);

    /// <summary>
    /// The command manifest this component should serve. It lands in the commands subdirectory of
    /// whichever tree the descriptor went to, so one question answers both.
    /// </summary>
    public static string Commands(string componentId, string? configured = null) =>
        Resolve(
            Path.Combine(AnchorDirectory, "commands"),
            Path.Combine(LeafDirectory, "commands"),
            componentId + ".json",
            configured);

    private static string Resolve(string anchorDir, string leafDir, string file, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        string anchored = Path.Combine(anchorDir, file);
        return File.Exists(anchored) ? anchored : Path.Combine(leafDir, file);
    }
}
