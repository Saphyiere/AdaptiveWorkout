using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdaptiveWorkout.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the app's storage, workout flow, watch link and notice. The host decides where the database file lives,
    /// and registers its own <see cref="WatchLink"/> and <see cref="WorkoutNotice"/> first if it can reach a watch or show notifications.
    /// </summary>
    public static IServiceCollection AddAdaptiveWorkout(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new AppSettings());
        services.AddSingleton(new WorkoutStore(databasePath));
        services.TryAddSingleton<WatchLink>();
        services.TryAddSingleton<WorkoutNotice>();
        services.AddSingleton<WorkoutFlow>();
        return services;
    }
}
