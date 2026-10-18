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
    private readonly Lock recentGate = new();
    private readonly List<HeartRateSample> recent = [];

    /// <summary>The newest reading, or null before any arrived.</summary>
    public HeartRateSample? Latest { get; private set; }

    /// <summary>The two minutes of readings up to the newest, oldest first, for drawing the heart rate as a line.</summary>
    public IReadOnlyList<HeartRateSample> Recent
    {
        get
        {
            lock (recentGate) return [.. recent];
        }
    }

    /// <summary>Raised when a reading arrives, on whatever thread it arrived on.</summary>
    public event Action? Changed;

    /// <summary>True while the watch is sending heart rate (about once a second), so the watch can buzz instead of the phone.</summary>
    public bool Connected => Latest is { } latest && DateTimeOffset.Now - latest.At < TimeSpan.FromSeconds(15);

    /// <summary>Opens the watch app's heart rate screen for this workout.</summary>
    public virtual void Open(WorkoutSession session)
    {
    }

    /// <summary>Stops the watch's heart rate and closes its screen.</summary>
    public virtual void Close()
    {
    }

    /// <summary>Tells the watch what the workout is doing, so it can show the rest or set and buzz when a rest countdown ends.</summary>
    public virtual void Show(WorkoutStatus status)
    {
    }

    /// <summary>Shows the newest reading and saves them all.</summary>
    public void Receive(IReadOnlyList<HeartRateSample> readings)
    {
        if (readings.Count == 0) return;

        var newest = readings.MaxBy(r => r.At)!;
        if (Latest is null || newest.At >= Latest.At) Latest = newest;
        var oldest = Latest.At - TimeSpan.FromMinutes(2);
        lock (recentGate)
        {
            recent.AddRange([.. readings.Where(r => !recent.Exists(e => e.At == r.At))]);
            recent.Sort((a, b) => a.At.CompareTo(b.At));
            recent.RemoveAll(r => r.At < oldest);
        }

        Changed?.Invoke();
        store.AddHeartRateAsync(readings).ContinueWith(t => Debug.WriteLine($"AdaptiveWorkout: saving heart rate failed: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
    }
}
