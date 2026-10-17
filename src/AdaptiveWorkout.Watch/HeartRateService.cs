using AdaptiveWorkout.Core;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Wearable;
using Android.Hardware;
using Android.OS;
using Android.Util;

namespace AdaptiveWorkout.Watch;

/// <summary>
/// Reads heart rate during a workout, screen on or off, and sends it to the phone.
/// Readings wait here until the phone takes them, so a short disconnect loses nothing.
/// </summary>
[Service(ForegroundServiceType = ForegroundService.TypeHealth, Exported = false)]
public sealed class HeartRateService : Service, ISensorEventListener
{
    /// <summary>The phone workout's start in Unix milliseconds; missing when the watch app was opened on its own.</summary>
    public const string ExtraWorkoutStartedAt = "workoutStartedAt";

    private const int NotificationId = 1;
    private const string Channel = "workout";

    // Longer than any workout, so the watch stops even if the phone's Finish never arrives.
    private static readonly TimeSpan MaxWorkout = TimeSpan.FromHours(4);

    // An hour of readings at one a second.
    private const int MaxUnsent = 3600;

    private readonly List<HeartRateSample> unsent = [];
    private readonly Handler handler = new(Looper.MainLooper!);
    private SensorManager? sensors;
    private PowerManager.WakeLock? wakeLock;
    private string? phoneId;
    private bool sending;

    /// <summary>When the workout started: the phone's start time, or when the watch app was opened on its own. Empty when not running.</summary>
    public static DateTimeOffset? StartedAt { get; private set; }

    /// <summary>True when the phone started this workout, so the phone also ends it.</summary>
    public static bool FromPhone { get; private set; }

    public static int? Bpm { get; private set; }
    public static bool PhoneReachable { get; private set; } = true;

    /// <summary>Raised on the main thread whenever the values above change, and when the service stops.</summary>
    public static event Action? Changed;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var workoutStartedAt = intent?.GetLongExtra(ExtraWorkoutStartedAt, 0) ?? 0;
        if (workoutStartedAt > 0) (StartedAt, FromPhone) = (DateTimeOffset.FromUnixTimeMilliseconds(workoutStartedAt).ToLocalTime(), true);
        else StartedAt ??= DateTimeOffset.Now;

        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel(Channel, GetString(Resource.String.workout_channel), NotificationImportance.Low));
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var notification = new Notification.Builder(this, Channel)
            .SetSmallIcon(Resource.Drawable.appicon_foreground)
            .SetContentTitle(GetString(Resource.String.app_name))
            .SetContentText(GetString(Resource.String.workout_notification))
            .SetContentIntent(open)
            .SetOngoing(true)
            .Build();
        if (OperatingSystem.IsAndroidVersionAtLeast(34)) StartForeground(NotificationId, notification, ForegroundService.TypeHealth);
        else StartForeground(NotificationId, notification);

        if (sensors is null)
        {
            sensors = (SensorManager)GetSystemService(SensorService)!;
            if (sensors.GetDefaultSensor(SensorType.HeartRate) is { } sensor) sensors.RegisterListener(this, sensor, SensorDelay.Normal);

            // The sensor only reports while the CPU is awake, and the screen goes off between glances.
            wakeLock = ((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial, "AdaptiveWorkout:heart-rate")!;
            wakeLock.Acquire((long)MaxWorkout.TotalMilliseconds);
        }

        handler.RemoveCallbacksAndMessages(null);
        handler.PostDelayed(StopSelf, (long)MaxWorkout.TotalMilliseconds);
        Changed?.Invoke();
        return StartCommandResult.NotSticky;
    }

    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Values is not [var value, ..] || value <= 0 || e.Accuracy == SensorStatus.NoContact) return;

        Bpm = (int)Math.Round(value);
        unsent.Add(new HeartRateSample { At = DateTimeOffset.Now, Bpm = Bpm.Value });
        if (unsent.Count > MaxUnsent) unsent.RemoveRange(0, unsent.Count - MaxUnsent);
        Changed?.Invoke();
        if (!sending) _ = SendAsync();
    }

    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy)
    {
    }

    public override void OnDestroy()
    {
        sensors?.UnregisterListener(this);
        if (wakeLock?.IsHeld == true) wakeLock.Release();
        handler.RemoveCallbacksAndMessages(null);
        (StartedAt, FromPhone, Bpm, PhoneReachable) = (null, false, null, true);
        Changed?.Invoke();
        base.OnDestroy();
    }

    // Sends everything not yet sent. Runs on the main thread, one send at a time; readings that arrive meanwhile go with the next one.
    private async Task SendAsync()
    {
        sending = true;
        var batch = unsent.ToList();
        try
        {
            phoneId ??= (await WearableClass.GetNodeClient(this).GetConnectedNodesAsync()).FirstOrDefault()?.Id;
            if (phoneId is not null)
            {
                await WearableClass.GetMessageClient(this).SendMessageAsync(phoneId, WatchMessages.HeartRate, WatchMessages.EncodeReadings(FromPhone ? StartedAt : null, batch));
                var sent = batch.ToHashSet();
                unsent.RemoveAll(sent.Contains);
            }

            PhoneReachable = phoneId is not null;
        }
        catch (Exception ex)
        {
            Log.Warn("AdaptiveWorkout", $"Sending heart rate failed: {ex.Message}");
            (phoneId, PhoneReachable) = (null, false);
        }
        finally
        {
            sending = false;
        }

        Changed?.Invoke();
    }
}
