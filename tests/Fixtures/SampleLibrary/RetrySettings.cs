using TheKrystalShip.KGSM.ComponentConfig;

namespace SampleLibrary;

/// <summary>
/// A bound section living in a library rather than in the leaf's entry assembly — the shape kgsm-bot
/// and kgsm-api both have, where the configuration types sit a layer below the host that binds them.
/// </summary>
[ConfigSection("Retry")]
public sealed class RetrySettings
{
    /// <summary>How many attempts before giving up.</summary>
    /// <panel>How many times a failed call is retried before the leaf gives up on it.</panel>
    [ConfigField("retryAttempts", "Retry attempts", Group = "net", Min = 0)]
    public int? Attempts { get; set; }
}
