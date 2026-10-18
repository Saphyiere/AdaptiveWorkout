namespace AdaptiveWorkout.Core;

/// <summary>
/// Am I doing better? Each exercise in a workout against the last workout that had it, as one number per workout, and the
/// trend those numbers make across all my exercises. A weight exercise counts as its sets' average estimated one-rep max (weight × (1 + reps / 30)),
/// so heavier and more reps both count, however the sets were split; a time exercise counts as its average set length.
/// </summary>
public static class Progress
{
    /// <summary>
    /// How <paramref name="session"/> compares with the last earlier workout of each of its exercises: 0.03 is 3% better.
    /// It's the geometric mean, so one exercise up 10% and another down by the same ratio come out even. Bodyweight and
    /// assisted sets lift the body too: they count the weigh-in nearest before the workout, the same for both workouts.
    /// Change is empty when no exercise had a last time.
    /// </summary>
    public static (double? Change, int Compared) Improvement(WorkoutSession session, IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<WorkoutSet> sets,
        IReadOnlyDictionary<long, Exercise> exercises, IReadOnlyList<BodyWeightEntry> bodyWeights)
    {
        var weighIn = bodyWeights.Where(w => w.At <= session.StartedAt).MaxBy(w => w.At) ?? bodyWeights.MinBy(w => w.At);
        double? bodyWeight = weighIn is null ? null : WorkoutStats.Pounds(weighIn.Weight, weighIn.WeightUnit);
        var earlier = sessions.Where(s => s.StartedAt < session.StartedAt).ToList();
        var ratios = new List<double>();
        foreach (var now in sets.Where(s => s.SessionId == session.Id).GroupBy(s => s.ExerciseId))
        {
            if (!exercises.TryGetValue(now.Key, out var exercise)) continue;
            if (ExerciseHistory.Recent(now.Key, earlier, sets, session.Id, 1) is not [var last]) continue;

            // Any set without a weight, or assisted, makes it a bodyweight exercise: each set lifts the body plus its weight.
            // Without a weigh-in, plain bodyweight sets still compare by reps; added or assisted weight can't.
            var both = now.Concat(last.Sets).ToList();
            var bodyweightExercise = exercise.Measure == ExerciseMeasure.Weight && both.Any(s => s.Weight is null or < 0);
            if (bodyweightExercise && bodyWeight is null && both.Any(s => s.Weight is not null)) continue;

            var body = bodyweightExercise ? bodyWeight ?? 1 : 0;
            double? Score(IEnumerable<WorkoutSet> exerciseSets)
            {
                var scores = new List<double>();
                foreach (var set in exerciseSets)
                {
                    var load = body + WorkoutStats.Pounds(set.Weight ?? 0, set.WeightUnit);
                    if (exercise.Measure == ExerciseMeasure.Time && set.DurationSeconds is int seconds and > 0) scores.Add(seconds);
                    else if (exercise.Measure == ExerciseMeasure.Weight && set.Reps is int reps and > 0 && load > 0) scores.Add(load * (1 + reps / 30.0));
                }

                return scores.Count == 0 ? null : scores.Average();
            }

            if (Score(now) is { } scoreNow && Score(last.Sets) is { } scoreThen) ratios.Add(scoreNow / scoreThen);
        }

        return (ratios.Count == 0 ? null : Math.Exp(ratios.Average(Math.Log)) - 1, ratios.Count);
    }

    /// <summary>
    /// Finished workouts with an improvement, oldest first, each with how much better my exercises were by then than the first
    /// time I did them, on average: 0.06 is 6% better. Each exercise's ups and downs add up to its change since its first time,
    /// and the total is their average, so it doesn't grow just because there were more workouts.
    /// </summary>
    public static List<(WorkoutSession Session, double Total)> Trend(IEnumerable<WorkoutSession> sessions, IEnumerable<WorkoutSet> sets)
    {
        var exercisesBySession = sets.ToLookup(s => s.SessionId, s => s.ExerciseId);
        var seen = new HashSet<long>();
        var compared = new HashSet<long>();
        var logs = 0.0;
        var trend = new List<(WorkoutSession Session, double Total)>();
        foreach (var session in sessions.Where(s => s.FinishedAt is not null).OrderBy(s => s.StartedAt))
        {
            foreach (var exerciseId in exercisesBySession[session.Id].Distinct())
            {
                if (!seen.Add(exerciseId)) compared.Add(exerciseId);
            }

            if (session is not { Improvement: { } change, ExercisesCompared: { } count }) continue;

            // The workout's improvement is the geometric mean over its exercises, so this adds each one's own change.
            logs += Math.Log(1 + change) * count;
            trend.Add((session, Math.Exp(logs / Math.Max(compared.Count, 1)) - 1));
        }

        return trend;
    }
}
