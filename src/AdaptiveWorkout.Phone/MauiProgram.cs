using AdaptiveWorkout.UI;
using AdaptiveWorkout.UI.Watch;
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
		builder.Services.AddAdaptiveWorkout(Path.Combine(FileSystem.AppDataDirectory, "adaptiveworkout.db"));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
