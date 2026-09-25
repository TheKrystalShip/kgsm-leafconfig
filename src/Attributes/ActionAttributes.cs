// A component's action manifest, declared where the actions happen.
//
// A component performs actions — `reactor:rules.write`, `monitor:thresholds.write` — and it performs
// other components' actions as its own service account, by calling their client packages. Both are
// declared on the code that does them: [Action] on the handler that performs one, [Requires] beside
// the call that performs another component's. The generator reads them back out of the compiled
// assembly and writes the manifest the node reports to the auth anchor, so the catalog of what can be
// granted is exactly what the code does.
//
// [Performs] is the client package's half: it marks the method that performs a component's action, so
// the generator can find every call to it and demand a [Requires] beside each one. Nothing is called
// without being required.
//
// Like the config attributes these are compiled in as source and never read at runtime.
//
// Format and rules: kgsm-docs/reference/action-manifest.md.

namespace TheKrystalShip.KGSM.ComponentConfig;

/// <summary>What performing an action does. Grants nothing; decides what a stale member still serves.</summary>
internal enum DeclaredEffect
{
    /// <summary>Observes and changes nothing.</summary>
    Read,

    /// <summary>Changes stored state.</summary>
    Write,

    /// <summary>Makes something happen.</summary>
    Execute,
}

/// <summary>The narrowest scope at which granting an action means something.</summary>
internal enum DeclaredScope
{
    Cluster,
    Node,
    Instance,
}

/// <summary>
/// Declares an action this component performs. On the handler that performs it, on the type holding
/// its handlers, or on the assembly for an action with no single handler — a field redacted from a
/// read.
/// </summary>
/// <remarks>
/// The id is this component's own: <c>rules.write</c>, or spelled in full as
/// <c>reactor:rules.write</c> when a constant from the component's client package is the natural thing
/// to name. It is <b>immutable in meaning</b> — an action that starts doing something else is a new id,
/// because every grant of the old one was made for what it used to mean.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ActionAttribute(string id, string title, DeclaredEffect effect, DeclaredScope scope) : Attribute
{
    /// <summary>The action's id, local (<c>rules.write</c>) or in full (<c>reactor:rules.write</c>).</summary>
    public string Id { get; } = id;

    /// <summary>What the role editor shows. Short, imperative: <c>Change reactor rules</c>.</summary>
    public string Title { get; } = title;

    public DeclaredEffect Effect { get; } = effect;

    public DeclaredScope Scope { get; } = scope;

    /// <summary>
    /// True for an action that only ever reads or changes the caller's own records. Every active person
    /// holds it; it is never filed into a permission, and the component performing it is what keeps it
    /// to the caller's own records.
    /// </summary>
    public bool Self { get; set; }
}

/// <summary>
/// Declares another component's action this component performs as its own service account, beside the
/// call that performs it — on the method making the call, on its type, or on the assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
internal sealed class RequiresAttribute(string action, DeclaredScope scope, string why) : Attribute
{
    /// <summary>The other component's action id, in full: <c>kgsm:server.restart</c>.</summary>
    public string Action { get; } = action;

    /// <summary>The scope kind the action is needed at.</summary>
    public DeclaredScope Scope { get; } = scope;

    /// <summary>Why, for the person reviewing the service's requirements.</summary>
    public string Why { get; } = why;
}

/// <summary>
/// Marks a client-package method as performing a component's action. Every call to it from a component
/// needs a <see cref="RequiresAttribute"/> for that action beside it, or the component's build fails.
/// </summary>
/// <remarks>
/// On the interface method a consumer calls. A call to a class implementing it is matched through the
/// interface, so marking the interface covers both.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class PerformsAttribute(string action) : Attribute
{
    /// <summary>The action id, in full.</summary>
    public string Action { get; } = action;
}

/// <summary>
/// Marks a setting that switches automated behaviour on — a cadence, an update check, a mode.
/// </summary>
/// <remarks>
/// Whoever last sets it is recorded as the author of what it switches on, and that automation runs only
/// while its author may still do what it does. Its default must be off: behaviour a component ships runs
/// only once a person has turned it on and become its author.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class AutomatesAttribute : Attribute;

/// <summary>
/// Names another assembly this component's actions and requirements are declared in — a library
/// holding its handlers or its calls.
/// </summary>
/// <remarks>
/// Opt-in for the same reason as <see cref="ConfigSectionAssemblyAttribute"/>: components share
/// libraries, and one component must not be described by another's declarations.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class ActionAssemblyAttribute(string name) : Attribute
{
    /// <summary>Simple assembly name, without the extension.</summary>
    public string Name { get; } = name;
}
