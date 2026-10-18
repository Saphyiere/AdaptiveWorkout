using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using AndroidManifest = Android.Manifest;

namespace AdaptiveWorkout.Phone;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // The page draws behind the status and navigation bars, and Logbook's paper is light, so their icons are dark. A dark theme flips these.
        var bars = WindowCompat.GetInsetsController(Window!, Window!.DecorView)!;
        bars.AppearanceLightStatusBars = true;
        bars.AppearanceLightNavigationBars = true;

        // The workout notification (rest and set timers on the lock screen) needs this from Android 13.
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(AndroidManifest.Permission.PostNotifications) != Permission.Granted) RequestPermissions([AndroidManifest.Permission.PostNotifications], 0);
    }
}
