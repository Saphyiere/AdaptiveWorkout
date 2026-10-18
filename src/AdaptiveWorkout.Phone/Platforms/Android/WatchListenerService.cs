using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;
using AdaptiveWorkout.UI.Workouts;
using Android.App;
using Android.Gms.Wearable;

namespace AdaptiveWorkout.Phone;

/// <summary>Hears the watch, whether the app is on screen, in the background, or not running: heart rate, and "start the next set".</summary>
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
        if (path is WatchMessages.Next or WatchMessages.Hello)
        {
            // On the main thread, like the screens, since the open workout isn't shared across threads.
            var workout = services.GetRequiredService<WorkoutFlow>();
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await workout.LoadAsync();
                if (path == WatchMessages.Next) workout.StartNext();

                // Answers every message, also a Next with nothing to start, so the watch's Start button never waits on it.
                if (workout.Status is { } status) watch.Show(status);
            });
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
