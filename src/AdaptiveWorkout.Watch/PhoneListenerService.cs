using System.Globalization;
using System.Text;
using AdaptiveWorkout.Core;
using Android.Content;
using Android.Gms.Wearable;

namespace AdaptiveWorkout.Watch;

/// <summary>Hears the phone when a workout starts or finishes, whether or not this app is running.</summary>
[Service(Exported = true)]
[IntentFilter(["com.google.android.gms.wearable.MESSAGE_RECEIVED"], DataScheme = "wear", DataHost = "*", DataPathPrefix = "/workout")]
public sealed class PhoneListenerService : WearableListenerService
{
    public const int StartNotificationId = 2;
    private const string StartChannel = "start";

    public override void OnMessageReceived(IMessageEvent message)
    {
        if (message.Path == WatchMessages.Stop) StopService(new Intent(this, typeof(HeartRateService)));
        if (message.Path != WatchMessages.Start || MainActivity.IsVisible) return;

        // The phone opens the heart rate screen itself. In case that didn't get through, this notification does:
        // it opens the screen straight away while the watch screen is off, and is one tap away otherwise.
        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(Encoding.UTF8.GetString(message.GetData()!), CultureInfo.InvariantCulture));
        var screen = new Intent(Intent.ActionView, Android.Net.Uri.Parse(WatchMessages.WorkoutUri(startedAt))!, this, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask);
        var open = PendingIntent.GetActivity(this, 0, screen, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel(StartChannel, GetString(Resource.String.start_channel), NotificationImportance.High));
        notifications.Notify(StartNotificationId, new Notification.Builder(this, StartChannel)
            .SetSmallIcon(Resource.Drawable.appicon_foreground)
            .SetContentTitle(GetString(Resource.String.start_channel))
            .SetContentText(GetString(Resource.String.start_notification))
            .SetContentIntent(open)
            .SetFullScreenIntent(open, true)
            .SetAutoCancel(true)
            .Build());
    }
}
