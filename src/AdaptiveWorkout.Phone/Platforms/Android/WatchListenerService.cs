using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Android.App;
using Android.Gms.Wearable;

namespace AdaptiveWorkout.Phone;

/// <summary>
/// Hears the watch, whether the app is on screen, in the background, or not running: heart rate; its button, which stops the
/// running set or starts the next one; and the end of a rest countdown, which starts the next set with the phone in a pocket.
/// </summary>
[Service(Exported = true)]
[IntentFilter(["com.google.android.gms.wearable.MESSAGE_RECEIVED"], DataScheme = "wear", DataHost = "*", DataPathPrefix = WatchMessages.HeartRate)]
[IntentFilter(["com.google.android.gms.wearable.MESSAGE_RECEIVED"], DataScheme = "wear", DataHost = "*", DataPathPrefix = "/workout")]
public sealed class WatchListenerService : WearableListenerService
{
    public override void OnMessageReceived(IMessageEvent message)
    {
        var services = IPlatformApplication.Current!.Services;
        var watch = services.GetRequiredService<WatchLink>();
        var path = message.Path;
        if (path is WatchMessages.Next or WatchMessages.StopSet or WatchMessages.RestOver or WatchMessages.Hello)
        {
            // On the main thread, like the screens, since the open workout isn't shared across threads.
            var workout = services.GetRequiredService<WorkoutFlow>();
            var data = message.GetData();
            var answered = new TaskCompletionSource();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await workout.LoadAsync();
                    if (path == WatchMessages.Next) workout.StartNext();
                    if (path == WatchMessages.StopSet) workout.StopSet();
                    if (path == WatchMessages.RestOver) workout.RestIsUp(WatchMessages.DecodeTime(data!));
                    if (path == WatchMessages.Hello && watch is PhoneWatchLink phone) phone.ShowTheme();

                    // Answers every message, also a Next with nothing to start, so the watch's Start button never waits on it.
                    if (workout.Status is { } status) watch.Show(status);
                }
                finally
                {
                    answered.SetResult();
                }
            });

            // Holds this call (Android makes it on a background thread) until the workout has moved on and the answer is on its way:
            // with the app in the background, Android may put it to sleep again soon after the call returns.
            if (!MainThread.IsMainThread) answered.Task.Wait(TimeSpan.FromSeconds(10));
            return;
        }

        var (workoutStartedAt, readings) = WatchMessages.DecodeReadings(message.GetData()!);
        watch.Receive(readings);
        if (workoutStartedAt is null) return;

        // The watch thinks a phone workout is still on. If none is open, the stop didn't reach the watch: send it again.
        // Reads storage rather than loading the flow, which the screen may be loading at the same moment.
        var flow = services.GetRequiredService<WorkoutFlow>();
        var store = services.GetRequiredService<WorkoutStore>();
        _ = Task.Run(async () =>
        {
            if (flow.Current is null && (await store.GetSessionsAsync()).All(s => s.FinishedAt is not null)) watch.Close();
        });
    }
}
