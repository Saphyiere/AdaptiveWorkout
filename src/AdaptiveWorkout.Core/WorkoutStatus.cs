namespace AdaptiveWorkout.Core;

/// <summary>What a workout is doing right now, as the watch and the phone's notification show it.</summary>
/// <param name="Resting">False while a set (or a superset's round) runs, which the watch can stop; true otherwise: between sets, the warm-up and the cool-down.</param>
/// <param name="Title">"Rest", the running set's exercise (a superset's: "Pull-ups + Dips"), "Warm-up" or "Cool-down".</param>
/// <param name="Since">When the set, the rest, the warm-up or the cool-down started.</param>
/// <param name="RestEndsAt">When the rest countdown reaches zero; empty when the rest counts up.</param>
/// <param name="Next">What starts next, if anything: an exercise, a superset's ("Pull-ups + Dips"), or "cool-down".</param>
public sealed record WorkoutStatus(bool Resting, string Title, DateTimeOffset Since, DateTimeOffset? RestEndsAt, string? Next);
