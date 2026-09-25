using SampleEngineClient;

using TheKrystalShip.KGSM.ComponentConfig;

// The component the action manifest's reference example describes: one action it performs, and one
// engine action it performs as its own service account. CarelessComponent compiles this same file with
// CARELESS defined, which removes every declaration and leaves the code — so its build is this one's
// with the attributes taken away.

[assembly: Leaf(
    id: "reactor",
    displayName: "Reactor",
    unit: "kgsm-reactor.service",
    role: "A fixture reactor that restarts a crashed server when a rule fires.")]

[assembly: ConfigFloorSource("appsettings", "/opt/kgsm-reactor/kgsm-reactor.settings.json")]

namespace SampleReactor;

public static class ReactorActions
{
    public const string RulesWrite = "reactor:rules.write";
}

/// <summary>Stands in for the evaluator: what matters is the action id the handler checks.</summary>
public static class Access
{
    public static bool Check(string action) => action.Length > 0;
}

[ConfigSection("Reactor")]
public sealed class ReactorSettings
{
    /// <panel>Whether rules act on what they see, rather than only recording it.</panel>
    [ConfigField("enforce", "Enforce rules")]
    [Automates]
    public bool Enforce { get; set; }
}

public sealed class RulesEndpoint
{
#if !CARELESS
    [Action(ReactorActions.RulesWrite, "Change reactor rules", DeclaredEffect.Write, DeclaredScope.Node)]
#endif
    public bool Write() => Access.Check(ReactorActions.RulesWrite);
}

/// <summary>A call from an async method: the call itself sits in the state machine the compiler made.</summary>
public sealed class Restarter(IEngine engine)
{
#if !CARELESS
    [Requires(EngineActions.ServerRestart, DeclaredScope.Instance, "Restart a crashed server when a rule fires")]
#endif
    public async Task OnCrashAsync(string instance)
    {
        await engine.RestartAsync(instance);
    }

    /// <summary>Calls only what performs nothing, so needs nothing.</summary>
    public string Look(string instance) => engine.Describe(instance);
}

/// <summary>
/// A call from a lambda, through the concrete class rather than the interface, covered by a declaration
/// on the type.
/// </summary>
#if !CARELESS
[Requires(EngineActions.ServerRestart, DeclaredScope.Instance, "Restart a crashed server when a rule fires")]
#endif
public sealed class Sweeper(Engine engine)
{
    public Task Run(IEnumerable<string> instances) =>
        Task.WhenAll(instances.Select(name => Task.Run(() => engine.RestartAsync(name))));
}
