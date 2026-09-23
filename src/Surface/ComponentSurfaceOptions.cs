namespace TheKrystalShip.KGSM.ComponentSurface;

/// <summary>
/// Where a component's own surface lives on disk.
/// </summary>
/// <remarks>
/// <para>
/// Two paths and nothing else. Everything else this library needs — the component's id, its systemd
/// unit, its groups, its fields and the floor sources to read — is declared in the descriptor its own
/// build generated, because a second record of any of it is a second thing to disagree with the first.
/// </para>
/// <para>
/// A component names these in its own settings, so the same build serves its surface whether it was
/// deployed from a checkout or installed from a package.
/// </para>
/// </remarks>
/// <param name="DescriptorPath">The descriptor this component's deploy installed — under
/// <c>/var/lib/kgsm/leaves/</c> for a leaf, <c>/var/lib/kgsm/anchors/</c> for an anchor. The file the
/// deploy left, never one beside the binary: a descriptor that ships with the build can describe a
/// different build than the one running, and this way every reader sees the same bytes.</param>
/// <param name="OverridePath">The env file the Control Panel's changes are written to, and that a
/// systemd drop-in reads back with <c>EnvironmentFile=-</c>. <b>The file is the store</b> — the writer
/// and the reader are the same process, so a second copy of what is overridden would only be a second
/// thing to disagree with the file.</param>
/// <param name="CommandsPath">The command manifest this component's deploy installed beside its
/// descriptor, or null for a component that declares no commands. Passed through to whoever asks
/// rather than modelled: the manifest is a file format a component ships on disk, and a typed copy
/// here would be a second statement of the same schema, free to disagree with the file the build
/// wrote.</param>
public sealed record ComponentSurfaceOptions(
    string DescriptorPath,
    string OverridePath,
    string? CommandsPath = null);
