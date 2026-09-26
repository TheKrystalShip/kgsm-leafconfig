using Microsoft.AspNetCore.Http;

namespace TheKrystalShip.KGSM.ComponentSurface.Http;

/// <summary>
/// Who is making a request to a component's own surface, as the account that authors what they set.
/// </summary>
/// <remarks>
/// <para>
/// The component registers one because only it knows what it gates with. An anchor reads the account
/// off the session it verified; a leaf, reached over a socket nobody else can open, reads what the node's
/// API relays. A component that registers none records no author, and every <c>[Automates]</c> setting
/// changed through it is left with nobody to authorize it.
/// </para>
/// </remarks>
public interface IComponentCaller
{
    /// <summary>The account making this request, or null when there is none to name.</summary>
    string? AccountOf(HttpContext context);
}

/// <summary>
/// The caller a node's API relays to a leaf, in the <c>Kgsm-Acting-Account</c> header.
/// </summary>
/// <remarks>
/// For a leaf only. The header is trusted because the socket it arrives on is reachable by the node's
/// API alone; on a surface served over the network anybody can send it, and an anchor names its caller
/// from the session it verified instead.
/// </remarks>
public sealed class RelayedComponentCaller : IComponentCaller
{
    /// <summary>The header a node's API names the account it is relaying for in.</summary>
    public const string Header = "Kgsm-Acting-Account";

    /// <inheritdoc />
    public string? AccountOf(HttpContext context) =>
        context.Request.Headers.TryGetValue(Header, out var value) && !string.IsNullOrWhiteSpace(value.ToString())
            ? value.ToString().Trim()
            : null;
}
