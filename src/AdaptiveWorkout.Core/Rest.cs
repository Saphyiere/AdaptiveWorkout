namespace AdaptiveWorkout.Core;

/// <summary>Rest between sets, for the rest countdown.</summary>
public static class Rest
{
    /// <summary>The countdown when there's nothing to go on.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How long I usually rested after this exercise in one session: the middle of the gaps between one of its sets stopping
    /// and the next set starting, to the nearest 15 seconds, between 30 seconds and 5 minutes. Null with no rest to go on.
    /// Gaps of 15 minutes or more were breaks, not rests.
    /// </summary>
    public static TimeSpan? Typical(IReadOnlyList<WorkoutSet> sessionSets, long exerciseId)
    {
        var ordered = sessionSets.OrderBy(s => s.Order).ToList();
        var gaps = ordered.Zip(ordered.Skip(1))
            .Where(pair => pair.First.ExerciseId == exerciseId && pair.First.StoppedAt is not null && pair.Second.StartedAt is not null)
            .Select(pair => pair.Second.StartedAt!.Value - pair.First.StoppedAt!.Value)
            .Where(gap => gap > TimeSpan.Zero && gap < TimeSpan.FromMinutes(15))
            .Order()
            .ToList();
        if (gaps.Count == 0) return null;

        return TimeSpan.FromSeconds(Math.Clamp(Math.Round(gaps[gaps.Count / 2].TotalSeconds / 15) * 15, 30, 300));
    }
}
