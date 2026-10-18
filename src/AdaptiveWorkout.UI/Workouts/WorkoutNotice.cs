using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.UI.Workouts;

/// <summary>
/// The workout outside the app, so the phone can stay in a pocket: the phone app shows it as a notification with a live
/// rest or set timer, on the lock screen too, and buzzes when a rest is up and no watch is there to. Other hosts show nothing.
/// </summary>
public class WorkoutNotice
{
    /// <summary>Shows what the workout is doing now; null takes the notice away.</summary>
    public virtual void Show(WorkoutStatus? status)
    {
    }

    /// <summary>Buzzes the phone, also with its screen off: the rest countdown is up and no watch is connected to buzz instead.</summary>
    public virtual void Buzz()
    {
    }
}
