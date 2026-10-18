using AdaptiveWorkout.UI;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidManifest = Android.Manifest;

namespace AdaptiveWorkout.Phone;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // For the opening's timing (AppLaunch): MAUI has made the window and its page. Their views, the web view among them, come next.
        IPlatformApplication.Current!.Services.GetRequiredService<AppLaunch>().Step("window");

        // The page draws behind the status and navigation bars, so their icons follow its theme: dark on Logbook's paper, light on Telemetry.
        PhoneThemeSetting.DressBars(Window!, PhoneThemeSetting.Last);

        // The workout notification (rest and set timers on the lock screen) needs this from Android 13.
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && CheckSelfPermission(AndroidManifest.Permission.PostNotifications) != Permission.Granted) RequestPermissions([AndroidManifest.Permission.PostNotifications], 0);
    }
}
