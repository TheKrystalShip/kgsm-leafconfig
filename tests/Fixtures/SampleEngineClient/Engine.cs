using TheKrystalShip.KGSM.ComponentConfig;

namespace SampleEngineClient;

// A client package, shaped like kgsm-lib: the component's action ids as constants beside the calls
// that perform them, and each such call marked, so a component calling one is held to requiring it.

public static class EngineActions
{
    public const string ServerRestart = "kgsm:server.restart";
    public const string ServerStart = "kgsm:server.start";
}

public interface IEngine
{
    [Performs(EngineActions.ServerRestart)]
    Task RestartAsync(string instance);

    [Performs(EngineActions.ServerStart)]
    void Start(string instance);

    /// <summary>Performs nothing: a read every caller may make.</summary>
    string Describe(string instance);
}

public sealed class Engine : IEngine
{
    public Task RestartAsync(string instance) => Task.CompletedTask;

    public void Start(string instance)
    {
    }

    public string Describe(string instance) => instance;
}
