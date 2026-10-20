namespace AdaptiveWorkout.Core;

/// <summary>What a workout is doing right now, as the watch and the phone's notification show it.</summary>
/// <param name="Resting">True between sets (a superset's handoff and the warm-up included), false while a set or the cool-down runs.</param>
/// <param name="Title">"Rest", "Pull-ups done" between a superset's halves, the running set's exercise, "Warm-up" or "Cool-down".</param>
/// <param name="Since">When the set, the rest, the warm-up or the cool-down started.</param>
/// <param name="RestEndsAt">When the rest countdown reaches zero; empty when the rest counts up.</param>
/// <param name="Next">The exercise that starts next, if any.</param>
public sealed record WorkoutStatus(bool Resting, string Title, DateTimeOffset Since, DateTimeOffset? RestEndsAt, string? Next);
