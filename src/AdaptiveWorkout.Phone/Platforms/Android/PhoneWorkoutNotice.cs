using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Workouts;
using Android.App;
using Android.Content;
using Android.OS;
using Application = Android.App.Application;

namespace AdaptiveWorkout.Phone;

/// <summary>
/// The workout in the notification bar and on the lock screen, with a timer the system keeps ticking on its own: a rest
/// counting down (or up), or the set's time. From Android 16 it asks to be a Live Update, which puts that timer in a status bar chip.
/// </summary>
public sealed class PhoneWorkoutNotice : WorkoutNotice
{
    private const int NotificationId = 1;
    private const string Channel = "workout";

    public override void Show(WorkoutStatus? status)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var context = Application.Context;
        var notifications = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (status is null)
        {
            notifications.Cancel(NotificationId);
            return;
        }

        // Default importance, since a Pixel keeps silent notifications out of the status bar and off the lock screen. Without a
        // sound or a buzz, though: it changes with every set and rest.
        var channel = new NotificationChannel(Channel, "Workout", NotificationImportance.Default);
        channel.SetSound(null, null);
        channel.EnableVibration(false);
        notifications.CreateNotificationChannel(channel);
        var app = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!)!;
        var builder = new Notification.Builder(context, Channel)
            .SetSmallIcon(Resource.Drawable.notification_mark)
            .SetContentTitle(status.Title)
            .SetContentText(status.Resting && status.Next is not null ? $"{status.Next} next" : null)
            .SetContentIntent(PendingIntent.GetActivity(context, 0, app, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent))
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetCategory("workout")
            .SetVisibility(NotificationVisibility.Public)
            .SetShowWhen(true)
            .SetUsesChronometer(true)
            .SetChronometerCountDown(status.RestEndsAt is not null)
            .SetWhen((status.RestEndsAt ?? status.Since).ToUnixTimeMilliseconds());

        // Notification.EXTRA_REQUEST_PROMOTED_ONGOING, which the .NET binding doesn't have yet. Without it, or with Live Updates
        // turned off for the app, this is an ordinary ongoing notification.
        var promoted = new Bundle();
        promoted.PutBoolean("android.requestPromotedOngoing", true);
        builder.AddExtras(promoted);
        notifications.Notify(NotificationId, builder.Build());
    }

    public override void Buzz()
    {
        // As an alarm: Android drops an ordinary buzz once the app is behind the lock screen or another app.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;

        var vibrator = ((VibratorManager)Application.Context.GetSystemService(Context.VibratorManagerService)!).DefaultVibrator;
        vibrator.Vibrate(VibrationEffect.CreateWaveform([0, 250, 120, 250], -1)!, VibrationAttributes.CreateForUsage((int)VibrationAttributesUsageType.Alarm));
    }
}
