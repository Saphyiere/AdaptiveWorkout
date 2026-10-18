using AdaptiveWorkout.UI;
using Android.OS;
using Android.Util;

namespace AdaptiveWorkout.Phone;

/// <summary>
/// Times the phone app's opening from when Android started its process, and writes the line to the log
/// (<c>adb logcat -s AdaptiveWorkout</c>). Also tells Android the app is fully drawn, which Android logs with its own count from
/// the tap (<c>adb logcat -s ActivityTaskManager</c>).
/// </summary>
public sealed class PhoneAppLaunch() : AppLaunch(TimeSpan.FromMilliseconds(SystemClock.UptimeMillis() - Process.StartUptimeMillis))
{
    protected override void Write(string line)
    {
        Log.Info("AdaptiveWorkout", line);
        MainThread.BeginInvokeOnMainThread(() => Platform.CurrentActivity?.ReportFullyDrawn());
    }
}
