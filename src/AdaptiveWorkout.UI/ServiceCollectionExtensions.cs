using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Themes;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdaptiveWorkout.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the app's storage, theme, workout flow, watch link and notice. The host decides where the database file lives,
    /// and registers its own <see cref="WatchLink"/>, <see cref="WorkoutNotice"/> and <see cref="ThemeSetting"/> first if it can reach
    /// a watch, show notifications, or has parts of its own to dress for the theme.
    /// </summary>
    public static IServiceCollection AddAdaptiveWorkout(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new AppSettings());
        services.AddSingleton(new WorkoutStore(databasePath));
        services.TryAddSingleton<WatchLink>();
        services.TryAddSingleton<WorkoutNotice>();
        services.TryAddSingleton<ThemeSetting>();
        services.AddSingleton<WorkoutFlow>();
        return services;
    }
}
