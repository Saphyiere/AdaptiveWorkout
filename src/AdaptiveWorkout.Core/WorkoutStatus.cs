namespace AdaptiveWorkout.Core;

/// <summary>What a workout is doing right now, as the watch and the phone's notification show it.</summary>
/// <param name="Resting">True between sets (a superset's handoff included), false while a set runs.</param>
/// <param name="Title">"Rest", "Pull-ups done" between a superset's halves, or the running set's exercise.</param>
/// <param name="Since">When the set or the rest started.</param>
/// <param name="RestEndsAt">When the rest countdown reaches zero; empty when the rest counts up.</param>
/// <param name="Next">The exercise that starts next, if any.</param>
public sealed record WorkoutStatus(bool Resting, string Title, DateTimeOffset Since, DateTimeOffset? RestEndsAt, string? Next);
