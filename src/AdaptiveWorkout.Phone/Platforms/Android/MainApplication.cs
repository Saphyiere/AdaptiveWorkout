using System.Diagnostics;
using AdaptiveWorkout.UI;
using Android.App;
using Android.Runtime;

namespace AdaptiveWorkout.Phone;

[Application]
public class MainApplication : MauiApplication
{
	// When the app's own code first runs, for the opening's timing: .NET is up. Set before the base constructor runs.
	private readonly long createdAt = Stopwatch.GetTimestamp();

	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp()
	{
		var app = MauiProgram.CreateMauiApp();
		var launch = app.Services.GetRequiredService<AppLaunch>();
		launch.Step(".NET", createdAt);
		launch.Step("app");
		return app;
	}
}
