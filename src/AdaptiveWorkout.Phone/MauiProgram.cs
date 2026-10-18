using AdaptiveWorkout.UI;
using AdaptiveWorkout.UI.Themes;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Microsoft.Extensions.Logging;

namespace AdaptiveWorkout.Phone;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<WatchLink, PhoneWatchLink>();
		builder.Services.AddSingleton<WorkoutNotice, PhoneWorkoutNotice>();
		builder.Services.AddSingleton<ThemeSetting, PhoneThemeSetting>();
		builder.Services.AddSingleton<AppLaunch, PhoneAppLaunch>();
		builder.Services.AddAdaptiveWorkout(Path.Combine(FileSystem.AppDataDirectory, "adaptiveworkout.db"));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
