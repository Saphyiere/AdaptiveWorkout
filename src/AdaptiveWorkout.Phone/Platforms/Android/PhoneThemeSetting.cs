using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Themes;
using AndroidX.Core.View;

namespace AdaptiveWorkout.Phone;

/// <summary>
/// Dresses the phone around the page for the theme: the status and navigation bar icons (dark on a light page, light on a dark
/// one) and the background shown while the page loads. The theme's id is also kept in Preferences, which the next start can read
/// before the app's own settings are open.
/// </summary>
public sealed class PhoneThemeSetting(WorkoutStore store) : ThemeSetting(store)
{
    private const string PreferenceKey = "theme";

    /// <summary>The theme the phone wore last, for the first moments of the next start.</summary>
    public static Theme Last => Theme.FromId(Preferences.Default.Get<string?>(PreferenceKey, null));

    /// <summary>Dark bar icons on a light page, light ones on a dark page.</summary>
    public static void DressBars(Android.Views.Window window, Theme theme)
    {
        var bars = WindowCompat.GetInsetsController(window, window.DecorView)!;
        bars.AppearanceLightStatusBars = !theme.Dark;
        bars.AppearanceLightNavigationBars = !theme.Dark;
    }

    protected override void ApplyToShell(Theme theme)
    {
        Preferences.Default.Set(PreferenceKey, theme.Id);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Platform.CurrentActivity?.Window is { } window) DressBars(window, theme);
            if (Application.Current?.Windows.FirstOrDefault()?.Page is MainPage page) page.ShowTheme(theme);
        });
    }
}
