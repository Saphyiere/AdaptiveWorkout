using System.Globalization;

namespace AdaptiveWorkout.Core;

public static class SetSummary
{
    /// <summary>
    /// One session's sets of one exercise, compactly: "3 × 10 at 95", "10, 10, 8 at 95", "8 at 95 · 10, 10 at 90".
    /// Time exercises list their durations: "5:00, 4:30".
    /// </summary>
    public static string Format(IReadOnlyList<WorkoutSet> sets, ExerciseMeasure measure)
    {
        if (measure == ExerciseMeasure.Time) return string.Join(", ", sets.Select(s => TimeText.Format(TimeSpan.FromSeconds(s.DurationSeconds ?? 0))));

        var groups = new List<(double? Weight, List<int> Reps)>();
        foreach (var set in sets)
        {
            if (groups.Count > 0 && groups[^1].Weight == set.Weight) groups[^1].Reps.Add(set.Reps ?? 0);
            else groups.Add((set.Weight, [set.Reps ?? 0]));
        }

        return string.Join(" · ", groups.Select(g =>
        {
            var reps = g.Reps.Count > 1 && g.Reps.Distinct().Count() == 1 ? $"{g.Reps.Count} × {g.Reps[0]}" : string.Join(", ", g.Reps);
            return $"{reps} at {WeightText(g.Weight)}";
        }));
    }

    /// <summary>"95", "47.5", "-40" (assisted), or "bodyweight".</summary>
    public static string WeightText(double? weight) => weight?.ToString("0.##", CultureInfo.InvariantCulture) ?? "bodyweight";
}

public static class TimeText
{
    /// <summary>"0:42", "12:05", "1:02:09".</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
