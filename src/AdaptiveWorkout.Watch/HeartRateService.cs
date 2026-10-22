using AdaptiveWorkout.Core;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Wearable;
using Android.Hardware;
using Android.OS;
using Android.Util;
using AndroidX.Core.App;
using AndroidX.Wear.Ongoing;

namespace AdaptiveWorkout.Watch;

/// <summary>
/// Reads heart rate during a workout, screen on or off, and sends it to the phone.
/// Readings wait here until the phone takes them, so a short disconnect loses nothing.
/// It also keeps the phone's word on what the workout is doing, and when a rest countdown is up, buzzes and starts the next set:
/// it keeps time while the phone's app may be asleep in a pocket.
/// </summary>
[Service(ForegroundServiceType = ForegroundService.TypeHealth, Exported = false)]
public sealed class HeartRateService : Service, ISensorEventListener
{
    /// <summary>The phone workout's start in Unix milliseconds; missing when the watch app was opened on its own.</summary>
    public const string ExtraWorkoutStartedAt = "workoutStartedAt";

    /// <summary>The "rest is up" notification, which brings the screen up when it isn't showing.</summary>
    public const int RestOverNotificationId = 3;

    private const int NotificationId = 1;
    private const string Channel = "workout";
    private const string RestOverChannel = "rest";

    // Longer than any workout, so the watch stops even if the phone's Finish never arrives.
    private static readonly TimeSpan MaxWorkout = TimeSpan.FromHours(4);

    // An hour of readings at one a second.
    private const int MaxUnsent = 3600;

    private readonly List<HeartRateSample> unsent = [];
    private readonly Handler handler = new(Looper.MainLooper!);
    private readonly Handler restAlarm = new(Looper.MainLooper!);
    private static HeartRateService? running;
    private DateTimeOffset? buzzAt;
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

    /// <summary>What the phone says the workout is doing; empty until it says.</summary>
    public static WorkoutStatus? Status { get; private set; }

    /// <summary>The status the watch last asked the phone to move on from. Until the phone answers with a new one, the button says so.</summary>
    public static WorkoutStatus? AskedOn { get; private set; }

    /// <summary>True once the rest countdown is up and the next set hasn't started.</summary>
    public static bool RestOver => Status is { Resting: true, RestEndsAt: { } end } && DateTimeOffset.Now >= end;

    /// <summary>Raised on the main thread whenever the values above change, and when the service stops.</summary>
    public static event Action? Changed;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        running = this;
        var workoutStartedAt = intent?.GetLongExtra(ExtraWorkoutStartedAt, 0) ?? 0;
        if (workoutStartedAt > 0) (StartedAt, FromPhone) = (DateTimeOffset.FromUnixTimeMilliseconds(workoutStartedAt).ToLocalTime(), true);
        else StartedAt ??= DateTimeOffset.Now;

        // The phone sends what the workout is doing on every change; this asks for it now, in case the last one came before this started.
        if (FromPhone) _ = TellPhoneAsync(WatchMessages.Hello, []);

        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel(Channel, GetString(Resource.String.workout_channel), NotificationImportance.Low));
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = new NotificationCompat.Builder(this, Channel);
        builder.SetSmallIcon(Resource.Drawable.appicon_foreground);
        builder.SetContentTitle(GetString(Resource.String.app_name));
        builder.SetContentText(GetString(Resource.String.workout_notification));
        builder.SetCategory(NotificationCompat.CategoryWorkout);
        builder.SetVisibility(NotificationCompat.VisibilityPublic);
        builder.SetContentIntent(open);
        builder.SetOngoing(true);

        // An ongoing activity keeps the heart rate screen up for the whole workout: without one, Wear OS goes back to the watch face
        // a while after the screen dims. If I go to the watch face myself, its icon there taps straight back in.
        var ongoing = new OngoingActivity.Builder(ApplicationContext!, NotificationId, builder);
        ongoing.SetStaticIcon(Resource.Drawable.appicon_foreground);
        ongoing.SetTouchIntent(open);
        ongoing.Build()!.Apply(ApplicationContext!);
        var notification = builder.Build()!;
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
        restAlarm.RemoveCallbacksAndMessages(null);
        ((NotificationManager)GetSystemService(NotificationService)!).Cancel(RestOverNotificationId);
        running = null;
        (StartedAt, FromPhone, Bpm, PhoneReachable, Status, AskedOn) = (null, false, null, true, null, null);
        Changed?.Invoke();
        base.OnDestroy();
    }

    /// <summary>Takes the phone's word for what the workout is doing, and sets the buzz for the end of a rest countdown. Any thread.</summary>
    public static void Show(WorkoutStatus status) => running?.handler.Post(() =>
    {
        if (running is not { } service) return;

        // With its rest screen open, the phone starts the next set at zero itself, which can land just before this buzz. Buzz anyway.
        var now = DateTimeOffset.Now;
        var startedAtZero = !status.Resting && service.buzzAt - now < TimeSpan.FromSeconds(1);
        Status = status;
        service.restAlarm.RemoveCallbacksAndMessages(null);
        service.buzzAt = status is { Resting: true, RestEndsAt: { } end, Next: not null } && end > now ? end : null;
        if (service.buzzAt is { } at) service.restAlarm.PostDelayed(service.RestIsOver, (long)Math.Ceiling((at - now).TotalMilliseconds));
        if (!RestOver) ((NotificationManager)service.GetSystemService(NotificationService)!).Cancel(RestOverNotificationId);
        if (startedAtZero) service.RestIsOver();
        else Changed?.Invoke();
    });

    /// <summary>
    /// Asks the phone to move the workout on: what its big button does (<see cref="WatchMessages.StopSet"/> or
    /// <see cref="WatchMessages.Next"/>), or <see cref="WatchMessages.RestOver"/>. The phone answers with the new status. Any thread.
    /// </summary>
    public static void Ask(string path, byte[]? payload = null) => running?.handler.Post(async () =>
    {
        if (running is not { } service) return;

        AskedOn = Status;
        Changed?.Invoke();
        PhoneReachable = await service.TellPhoneAsync(path, payload ?? []);
        if (!PhoneReachable) AskedOn = null;
        Changed?.Invoke();
    });

    // The rest countdown is up: buzz, start the next set on the phone (from here, since the phone's app may be asleep in a pocket;
    // if its rest screen is open it may have started it already, which is fine), and if the heart rate screen isn't showing, bring
    // it up. Out of the phone's reach, the screen shows "Rest is up" and Start for when it's back.
    private void RestIsOver()
    {
        buzzAt = null;
        var vibrator = OperatingSystem.IsAndroidVersionAtLeast(31) ? ((VibratorManager)GetSystemService(VibratorManagerService)!).DefaultVibrator : (Vibrator)GetSystemService(VibratorService)!;
        vibrator.Vibrate(VibrationEffect.CreateWaveform([0, 400, 200, 400, 200, 400], -1));
        if (Status is { Resting: true, RestEndsAt: { } end, Next: not null }) Ask(WatchMessages.RestOver, WatchMessages.EncodeTime(end));
        Changed?.Invoke();
        if (MainActivity.IsVisible) return;

        // It opens the screen straight away while the watch screen is off, and is one tap away otherwise. The buzz above is the only one.
        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        var channel = new NotificationChannel(RestOverChannel, GetString(Resource.String.rest_over_channel), NotificationImportance.High);
        channel.EnableVibration(false);
        channel.SetSound(null, null);
        notifications.CreateNotificationChannel(channel);
        var open = PendingIntent.GetActivity(this, 1, new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.NewTask), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        notifications.Notify(RestOverNotificationId, new Notification.Builder(this, RestOverChannel)
            .SetSmallIcon(Resource.Drawable.appicon_foreground)
            .SetContentTitle(GetString(Resource.String.rest_over))
            .SetContentText(Status is { Resting: true, Next: { } next } ? next : Status?.Title)
            .SetContentIntent(open)
            .SetFullScreenIntent(open, true)
            .SetAutoCancel(true)
            .Build());
    }

    // Sends everything not yet sent. Runs on the main thread, one send at a time; readings that arrive meanwhile go with the next one.
    private async Task SendAsync()
    {
        sending = true;
        var batch = unsent.ToList();
        PhoneReachable = await TellPhoneAsync(WatchMessages.HeartRate, WatchMessages.EncodeReadings(FromPhone ? StartedAt : null, batch));
        if (PhoneReachable)
        {
            var sent = batch.ToHashSet();
            unsent.RemoveAll(sent.Contains);
        }

        sending = false;
        Changed?.Invoke();
    }

    // Sends one message to the phone; false when it couldn't.
    private async Task<bool> TellPhoneAsync(string path, byte[] payload)
    {
        try
        {
            phoneId ??= (await WearableClass.GetNodeClient(this).GetConnectedNodesAsync()).FirstOrDefault()?.Id;
            if (phoneId is null) return false;

            await WearableClass.GetMessageClient(this).SendMessageAsync(phoneId, path, payload);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("AdaptiveWorkout", $"Reaching the phone failed: {ex.Message}");
            phoneId = null;
            return false;
        }
    }
}
