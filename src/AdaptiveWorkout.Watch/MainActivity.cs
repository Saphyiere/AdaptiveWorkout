using System.Globalization;
using AdaptiveWorkout.Core;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Activity;
using AndroidX.Wear.Ambient;

namespace AdaptiveWorkout.Watch;

/// <summary>
/// The heart rate screen. The phone opens it when a workout starts and closes it at Finish, and it comes up when a rest countdown is up.
/// When the watch screen times out it stays up, dimmed, instead of going back to the watch face.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTask)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "adaptiveworkout", DataHost = "workout")]
public class MainActivity : ComponentActivity, IAmbientLifecycleObserverAmbientLifecycleCallback
{
    private TextView bpm = null!;
    private TextView status = null!;
    private Chronometer elapsed = null!;
    private View phase = null!;
    private TextView phaseTitle = null!;
    private Chronometer phaseTime = null!;
    private Button start = null!;
    private long workoutStartedAt;
    private bool ambient;

    // The status Start was tapped on. The button says "Starting…" until the phone answers with a new one.
    private WorkoutStatus? startTappedOn;

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
        phase = FindViewById(Resource.Id.phase)!;
        phaseTitle = FindViewById<TextView>(Resource.Id.phase_title)!;
        phaseTime = FindViewById<Chronometer>(Resource.Id.phase_time)!;
        start = FindViewById<Button>(Resource.Id.start)!;
        start.Click += (_, _) =>
        {
            startTappedOn = HeartRateService.Status;
            HeartRateService.StartNext();
            Show();
        };
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
        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.Cancel(PhoneListenerService.StartNotificationId);
        notifications.Cancel(HeartRateService.RestOverNotificationId);
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
        if (CheckSelfPermission(HeartRatePermission) == Permission.Granted)
        {
            StartForegroundService(new Intent(this, typeof(HeartRateService)).PutExtra(HeartRateService.ExtraWorkoutStartedAt, workoutStartedAt));
            return;
        }

        status.Text = GetString(Resource.String.permission_needed);
        status.Visibility = ViewStates.Visible;
    }

    public void OnEnterAmbient(AmbientLifecycleObserverAmbientDetails ambientDetails)
    {
        ambient = true;
        Show();
    }

    public void OnExitAmbient()
    {
        ambient = false;
        Show();
    }

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
        // Dimmed, the pink and the button go, to spare the screen.
        bpm.Text = HeartRateService.Bpm?.ToString(CultureInfo.InvariantCulture) ?? GetString(Resource.String.no_reading);
        bpm.SetTextColor(GetColorStateList(ambient ? Resource.Color.ink : Resource.Color.accent));
        status.Text = HeartRateService.PhoneReachable ? "" : GetString(Resource.String.phone_unreachable);
        status.Visibility = HeartRateService.PhoneReachable ? ViewStates.Gone : ViewStates.Visible;

        if (HeartRateService.StartedAt is not { } startedAt) return;
        elapsed.Base = SystemClock.ElapsedRealtime() - (long)(DateTimeOffset.Now - startedAt).TotalMilliseconds;
        elapsed.Start();

        // What the phone says the workout is doing: a set and its time, or a rest counting down to the next set (then how far over).
        var workout = HeartRateService.Status;
        var canStart = workout is { Resting: true, Next: not null } && HeartRateService.PhoneReachable && !ambient;
        phase.Visibility = workout is null ? ViewStates.Gone : ViewStates.Visible;
        start.Visibility = canStart ? ViewStates.Visible : ViewStates.Gone;
        elapsed.Visibility = canStart ? ViewStates.Gone : ViewStates.Visible;
        if (workout is null) return;

        var over = HeartRateService.RestOver;
        var countdown = workout.RestEndsAt is not null && !over;
        phaseTitle.Text = over ? GetString(Resource.String.rest_over) : workout.Title;
        phaseTime.CountDown = countdown;
        phaseTime.Format = over ? "+%s" : null;
        phaseTime.SetTextColor(GetColorStateList(over && !ambient ? Resource.Color.accent : Resource.Color.ink));
        phaseTime.Base = SystemClock.ElapsedRealtime() - (long)(DateTimeOffset.Now - (countdown || over ? workout.RestEndsAt!.Value : workout.Since)).TotalMilliseconds;
        phaseTime.Start();

        // Out of the phone's reach, the button goes until it's back, then can be tapped again.
        if (!HeartRateService.PhoneReachable) startTappedOn = null;
        if (workout.Next is not { } next) return;

        var starting = ReferenceEquals(startTappedOn, workout);
        start.Enabled = !starting;
        start.Text = starting ? GetString(Resource.String.starting) : GetString(Resource.String.start_next, new Java.Lang.String(next));
    }
}
