using System.Globalization;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using AndroidX.Wear.Ambient;

namespace AdaptiveWorkout.Watch;

/// <summary>
/// The heart rate screen. The phone opens it when a workout starts and closes it at Finish.
/// When the watch screen times out it stays up, dimmed, instead of going back to the watch face.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "adaptiveworkout", DataHost = "workout")]
public class MainActivity : ComponentActivity, IAmbientLifecycleObserverAmbientLifecycleCallback
{
    private TextView bpm = null!;
    private TextView status = null!;
    private Chronometer elapsed = null!;
    private long workoutStartedAt;

    /// <summary>True while the screen is showing, so the phone's start message doesn't also post a notification.</summary>
    public static bool IsVisible { get; private set; }

    // Wear OS 6 (API 36) split heart rate out of the body sensors permission.
    private static string HeartRatePermission => OperatingSystem.IsAndroidVersionAtLeast(36) ? "android.permission.health.READ_HEART_RATE" : Android.Manifest.Permission.BodySensors;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);
        SetShowWhenLocked(true);
        SetTurnScreenOn(true);
        bpm = FindViewById<TextView>(Resource.Id.bpm)!;
        status = FindViewById<TextView>(Resource.Id.status)!;
        elapsed = FindViewById<Chronometer>(Resource.Id.elapsed)!;
        Lifecycle.AddObserver(AmbientLifecycleObserverKt.AmbientLifecycleObserver(this, this));
        StartReading(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        StartReading(intent);
    }

    protected override void OnStart()
    {
        base.OnStart();
        HeartRateService.Changed += OnChanged;
        Show();
    }

    protected override void OnResume()
    {
        base.OnResume();
        IsVisible = true;
        ((NotificationManager)GetSystemService(NotificationService)!).Cancel(PhoneListenerService.StartNotificationId);
    }

    protected override void OnPause()
    {
        IsVisible = false;
        base.OnPause();
    }

    protected override void OnStop()
    {
        HeartRateService.Changed -= OnChanged;
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        // Opened on the watch alone, closing the screen ends it. A phone workout keeps going until Finish on the phone.
        if (IsFinishing && !HeartRateService.FromPhone) StopService(new Intent(this, typeof(HeartRateService)));
        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (CheckSelfPermission(HeartRatePermission) == Permission.Granted) StartForegroundService(new Intent(this, typeof(HeartRateService)).PutExtra(HeartRateService.ExtraWorkoutStartedAt, workoutStartedAt));
        else status.Text = GetString(Resource.String.permission_needed);
    }

    public void OnEnterAmbient(AmbientLifecycleObserverAmbientDetails ambientDetails) => bpm.SetTextColor(GetColorStateList(Resource.Color.ink));

    public void OnExitAmbient() => bpm.SetTextColor(GetColorStateList(Resource.Color.accent));

    public void OnUpdateAmbient()
    {
    }

    // Asks for heart rate (and notifications, for the workout notice) the first time; after that, starts reading at once.
    private void StartReading(Intent? intent)
    {
        workoutStartedAt = intent?.Data?.GetQueryParameter("started") is { } started ? long.Parse(started, CultureInfo.InvariantCulture) : 0;
        string[] needed = OperatingSystem.IsAndroidVersionAtLeast(33) ? [HeartRatePermission, Android.Manifest.Permission.PostNotifications] : [HeartRatePermission];
        var missing = needed.Where(p => CheckSelfPermission(p) != Permission.Granted).ToArray();
        if (missing.Length > 0) RequestPermissions(missing, 0);
        else StartForegroundService(new Intent(this, typeof(HeartRateService)).PutExtra(HeartRateService.ExtraWorkoutStartedAt, workoutStartedAt));
    }

    private void OnChanged()
    {
        // Stopped: Finish on the phone, or the time limit.
        if (HeartRateService.StartedAt is null) FinishAndRemoveTask();
        else Show();
    }

    private void Show()
    {
        bpm.Text = HeartRateService.Bpm?.ToString(CultureInfo.InvariantCulture) ?? GetString(Resource.String.no_reading);
        status.Text = HeartRateService.PhoneReachable ? "" : GetString(Resource.String.phone_unreachable);

        if (HeartRateService.StartedAt is not { } startedAt) return;
        elapsed.Base = SystemClock.ElapsedRealtime() - (long)(DateTimeOffset.Now - startedAt).TotalMilliseconds;
        elapsed.Start();
    }
}
