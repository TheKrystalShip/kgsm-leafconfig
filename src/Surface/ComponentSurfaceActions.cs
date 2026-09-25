namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// The actions every component's standard surface performs: reading and changing its configuration,
/// reading its journal, and restarting it.
/// </summary>
/// <remarks>
/// <para>
/// Declared once, by the package that serves them: the generator writes them into every component's
/// action manifest, so no component declares them by hand. They sit in the component's own namespace —
/// <c>monitor:config.write</c> — at the scope it is administered at: the node for a leaf, the cluster
/// for an anchor.
/// </para>
/// <para>
/// Whoever serves the surface checks these: a leaf's node, which relays to it, or an anchor itself.
/// </para>
/// </remarks>
public static class ComponentSurfaceActions
{
    /// <summary>Read the component's configuration.</summary>
    public const string ConfigRead = "config.read";

    /// <summary>Change the component's configuration.</summary>
    public const string ConfigWrite = "config.write";

    /// <summary>Read the component's journal.</summary>
    public const string JournalRead = "journal.read";

    /// <summary>Restart the component.</summary>
    public const string LifecycleRestart = "lifecycle.restart";

    /// <summary>Every standard action's local id.</summary>
    public static IReadOnlyList<string> All { get; } = [ConfigRead, ConfigWrite, JournalRead, LifecycleRestart];

    /// <summary>A standard action's full id for <paramref name="component"/>: <c>monitor:config.write</c>.</summary>
    public static string For(string component, string action) => $"{component}:{action}";
}
