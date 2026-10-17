using System.Diagnostics;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;

namespace AdaptiveWorkout.UI.Watch;

/// <summary>
/// The watch during a workout: the phone opens the watch app when a workout starts and closes it at the end,
/// and the watch sends heart rate back. The phone app overrides <see cref="Open"/> and <see cref="Close"/>; other hosts have no watch.
/// </summary>
public class WatchLink(WorkoutStore store)
{
    /// <summary>The newest reading, or null before any arrived.</summary>
    public HeartRateSample? Latest { get; private set; }

    /// <summary>Raised when a reading arrives, on whatever thread it arrived on.</summary>
    public event Action? Changed;

    /// <summary>Opens the watch app's heart rate screen for this workout.</summary>
    public virtual void Open(WorkoutSession session)
    {
    }

    /// <summary>Stops the watch's heart rate and closes its screen.</summary>
    public virtual void Close()
    {
    }

    /// <summary>Shows the newest reading and saves them all.</summary>
    public void Receive(IReadOnlyList<HeartRateSample> readings)
    {
        if (readings.Count == 0) return;

        var newest = readings.MaxBy(r => r.At)!;
        if (Latest is null || newest.At >= Latest.At) Latest = newest;
        Changed?.Invoke();
        store.AddHeartRateAsync(readings).ContinueWith(t => Debug.WriteLine($"AdaptiveWorkout: saving heart rate failed: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
    }
}
