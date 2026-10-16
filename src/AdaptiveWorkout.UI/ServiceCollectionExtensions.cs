using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Extensions.DependencyInjection;

namespace AdaptiveWorkout.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the app's storage and workout flow. The host decides where the database file lives.</summary>
    public static IServiceCollection AddAdaptiveWorkout(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new AppSettings());
        services.AddSingleton(new WorkoutStore(databasePath));
        services.AddSingleton<WorkoutFlow>();
        return services;
    }
}
