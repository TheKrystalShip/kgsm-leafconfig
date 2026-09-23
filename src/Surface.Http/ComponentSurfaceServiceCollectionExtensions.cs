using Microsoft.Extensions.DependencyInjection;

namespace TheKrystalShip.KGSM.ComponentSurface.Http;

/// <summary>
/// Registers everything a component needs to answer for itself.
/// </summary>
public static class ComponentSurfaceServiceCollectionExtensions
{
    /// <summary>
    /// Adds the component's own surface — its descriptor, the host's floors beneath it, the overrides
    /// in force, its unit, its journal and the commands it declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One call, because a component that registered five of the six has a tab that answers 500 behind
    /// whatever gate it is mounted under, which reads as the whole surface being down.
    /// </para>
    /// <para>
    /// Every one is a singleton and every one is cheap to hold: the descriptor and the floors are
    /// cached reads with their own TTL, the override file IS the store, and the journal follower keeps
    /// one <c>journalctl</c> for all watchers and none when nobody is watching.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddComponentSurface(
        this IServiceCollection services, ComponentSurfaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return services.AddComponentSurface(_ => options);
    }

    /// <summary>
    /// The same, for a component whose paths are resolved from its own configuration and are therefore
    /// not in hand while the container is being built.
    /// </summary>
    public static IServiceCollection AddComponentSurface(
        this IServiceCollection services, Func<IServiceProvider, ComponentSurfaceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<ComponentDescriptorStore>();
        services.AddSingleton<ComponentOverrideStore>();
        services.AddSingleton<ComponentFloorReader>();
        services.AddSingleton<ComponentUnitControl>();
        services.AddSingleton<ComponentConfigService>();
        services.AddSingleton<ComponentUnitReader>();
        services.AddSingleton<ComponentJournal>();
        services.AddSingleton<ComponentJournalFollower>();
        services.AddSingleton<ComponentCommandManifest>();

        return services;
    }
}
