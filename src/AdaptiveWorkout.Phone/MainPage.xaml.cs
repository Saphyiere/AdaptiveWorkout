using AdaptiveWorkout.UI;
using AdaptiveWorkout.UI.Themes;

namespace AdaptiveWorkout.Phone;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
		ShowTheme(PhoneThemeSetting.Last);

		// For the opening's timing (AppLaunch): the web view exists, and starts loading the page.
		blazorWebView.BlazorWebViewInitialized += (_, _) => IPlatformApplication.Current!.Services.GetRequiredService<AppLaunch>().Step("web view");
	}

	/// <summary>The theme's background (app.css's ground) behind the page and in its web view, seen while the page loads.</summary>
	public void ShowTheme(Theme theme)
	{
		var ground = Color.FromArgb(theme.Ground);
		BackgroundColor = ground;
		blazorWebView.BackgroundColor = ground;
	}
}
