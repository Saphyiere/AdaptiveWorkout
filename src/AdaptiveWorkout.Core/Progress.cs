namespace AdaptiveWorkout.Core;

/// <summary>
/// A workout on an improvement trend: how it compared with the last time (<paramref name="Change"/>), and how much better the
/// exercises were by then than the first time, on average (<paramref name="Total"/>). 0.03 is 3% better.
/// </summary>
public sealed record TrendPoint(WorkoutSession Session, double Change, double Total);

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
        var earlier = sessions.Where(s => s.StartedAt < session.StartedAt).ToList();
        IReadOnlyList<WorkoutSet>? LastTime(long exerciseId) => ExerciseHistory.Recent(exerciseId, earlier, sets, session.Id, 1) is [var last] ? last.Sets : null;
        var now = sets.Where(s => s.SessionId == session.Id).GroupBy(s => s.ExerciseId);
        return Compare(now, LastTime, exercises, WeighIn(session, BodyWeight.InOrder(bodyWeights)));
    }

    /// <summary>
    /// Finished workouts with an improvement, oldest first, each with how much better my exercises were by then than the first
    /// time I did them, on average: 0.06 is 6% better. Each exercise's ups and downs add up to its change since its first time,
    /// and the total is their average, so it doesn't grow just because there were more workouts. All it needs of the sets is
    /// which exercises each workout had (<paramref name="exercisesDone"/>; repeats don't matter).
    /// </summary>
    public static List<TrendPoint> Trend(IEnumerable<WorkoutSession> sessions, IEnumerable<(long SessionId, long ExerciseId)> exercisesDone) =>
        Accumulate(sessions, exercisesDone, s => s.ExercisesCompared is int count ? (s.Improvement, count) : (null, 0));

    /// <summary>
    /// The trend of some of my workouts on their own, e.g. one session's, or one exercise's: each of <paramref name="sessions"/>
    /// against the last of them that had each of its exercises in <paramref name="sets"/>, with the rest of my workouts left out.
    /// So Monday's session compares with last Monday's, even when Thursday's had the same exercises in between.
    /// </summary>
    public static List<TrendPoint> Trend(IReadOnlyList<WorkoutSession> sessions, IReadOnlyList<WorkoutSet> sets, IReadOnlyDictionary<long, Exercise> exercises,
        IReadOnlyList<BodyWeightEntry> bodyWeights)
    {
        // Only the workouts with some of the sets count, e.g. the ones that had the exercise.
        var setsBySession = sets.ToLookup(s => s.SessionId);
        var done = sessions.Where(s => s.FinishedAt is not null && setsBySession.Contains(s.Id)).OrderBy(s => s.StartedAt).ToList();
        var weighIns = BodyWeight.InOrder(bodyWeights);
        var lastTime = new Dictionary<long, IReadOnlyList<WorkoutSet>>();
        var changes = new Dictionary<long, (double? Change, int Compared)>();
        foreach (var session in done)
        {
            var now = setsBySession[session.Id].GroupBy(s => s.ExerciseId).ToList();
            changes[session.Id] = Compare(now, lastTime.GetValueOrDefault, exercises, WeighIn(session, weighIns));
            foreach (var exerciseSets in now) lastTime[exerciseSets.Key] = [.. exerciseSets];
        }

        return Accumulate(done, sets.Select(s => (s.SessionId, s.ExerciseId)), s => changes[s.Id]);
    }

    // One workout's sets, by exercise, against each exercise's sets the last time (empty when there was none), as the geometric
    // mean of the ratios of their scores, and how many exercises it had.
    private static (double? Change, int Compared) Compare(IEnumerable<IGrouping<long, WorkoutSet>> setsByExercise,
        Func<long, IReadOnlyList<WorkoutSet>?> lastTime, IReadOnlyDictionary<long, Exercise> exercises, double? bodyWeight)
    {
        var ratios = new List<double>();
        foreach (var now in setsByExercise)
        {
            if (!exercises.TryGetValue(now.Key, out var exercise) || lastTime(now.Key) is not { } last) continue;

            // Any set without a weight, or assisted, makes it a bodyweight exercise: each set lifts the body plus its weight.
            // Without a weigh-in, plain bodyweight sets still compare by reps; added or assisted weight can't.
            var both = now.Concat(last).ToList();
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

            if (Score(now) is { } scoreNow && Score(last) is { } scoreThen) ratios.Add(scoreNow / scoreThen);
        }

        return (ratios.Count == 0 ? null : Math.Exp(ratios.Average(Math.Log)) - 1, ratios.Count);
    }

    // What the body weighed for a workout, in pounds (see BodyWeight.Nearest). Empty without any weigh-in.
    private static double? WeighIn(WorkoutSession session, List<BodyWeightEntry> weighIns) =>
        BodyWeight.Nearest(weighIns, session.StartedAt) is { } weighIn ? WorkoutStats.Pounds(weighIn.Weight, weighIn.WeightUnit) : null;

    // Adds the finished workouts' changes up, oldest first, into each one's total so far (see Trend). Ones without a change are left out.
    private static List<TrendPoint> Accumulate(IEnumerable<WorkoutSession> sessions, IEnumerable<(long SessionId, long ExerciseId)> exercisesDone,
        Func<WorkoutSession, (double? Change, int Compared)> improvement)
    {
        var exercisesBySession = exercisesDone.ToLookup(e => e.SessionId, e => e.ExerciseId);
        var seen = new HashSet<long>();
        var compared = new HashSet<long>();
        var logs = 0.0;
        var trend = new List<TrendPoint>();
        foreach (var session in sessions.Where(s => s.FinishedAt is not null).OrderBy(s => s.StartedAt))
        {
            foreach (var exerciseId in exercisesBySession[session.Id].Distinct())
            {
                if (!seen.Add(exerciseId)) compared.Add(exerciseId);
            }

            if (improvement(session) is not ({ } change, var count)) continue;

            // The workout's improvement is the geometric mean over its exercises, so this adds each one's own change.
            logs += Math.Log(1 + change) * count;
            trend.Add(new TrendPoint(session, change, Math.Exp(logs / Math.Max(compared.Count, 1)) - 1));
        }

        return trend;
    }
}
