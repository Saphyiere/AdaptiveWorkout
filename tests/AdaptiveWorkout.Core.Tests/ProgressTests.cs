using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class ProgressTests
{
    private const long Bench = 1, PullUps = 2, Plank = 3, Curls = 4;

    private static readonly DateTimeOffset Monday = new(2026, 10, 12, 18, 0, 0, TimeSpan.FromHours(-4));

    private static readonly Dictionary<long, Exercise> Exercises = new()
    {
        [Bench] = new() { Id = Bench, Name = "Bench press", Measure = ExerciseMeasure.Weight },
        [PullUps] = new() { Id = PullUps, Name = "Pull-ups", Measure = ExerciseMeasure.Weight },
        [Plank] = new() { Id = Plank, Name = "Plank", Measure = ExerciseMeasure.Time },
        [Curls] = new() { Id = Curls, Name = "Curls", Measure = ExerciseMeasure.Weight },
    };

    private readonly List<WorkoutSession> sessions = [];
    private readonly List<WorkoutSet> sets = [];

    // A finished workout in the given week. Count is reps, or seconds for a time exercise; an empty weight is bodyweight.
    private WorkoutSession Workout(int week, params (long ExerciseId, double? Weight, int Count)[] done)
    {
        var session = new WorkoutSession { Id = sessions.Count + 1, StartedAt = Monday.AddDays(7 * week), FinishedAt = Monday.AddDays(7 * week).AddHours(1) };
        sessions.Add(session);
        foreach (var (exerciseId, weight, count) in done)
        {
            var time = Exercises[exerciseId].Measure == ExerciseMeasure.Time;
            sets.Add(new WorkoutSet { SessionId = session.Id, ExerciseId = exerciseId, Order = sets.Count + 1, Weight = weight, Reps = time ? null : count, DurationSeconds = time ? count : null });
        }

        return session;
    }

    private (double? Change, int Compared) Improvement(WorkoutSession session, params BodyWeightEntry[] weighIns) => Progress.Improvement(session, sessions, sets, Exercises, weighIns);

    [Fact]
    public void Improvement_CountsHeavierWeight_AndMoreReps()
    {
        Workout(0, (Bench, 100, 10), (Bench, 100, 10), (Bench, 100, 10));
        var heavier = Workout(1, (Bench, 105, 10), (Bench, 105, 10), (Bench, 105, 10));
        Workout(2, (Curls, 30, 10), (Curls, 30, 10), (Curls, 30, 8));
        var moreReps = Workout(3, (Curls, 30, 10), (Curls, 30, 10), (Curls, 30, 10));

        Assert.Equal(0.05, Improvement(heavier).Change!.Value, 6);
        Assert.Equal(1, Improvement(heavier).Compared);

        // Average estimated max: 30 × (1⅓ + 1⅓ + 1 4/15) / 3 = 39.33 before, 40 after.
        Assert.Equal(0.016949, Improvement(moreReps).Change!.Value, 6);
    }

    [Fact]
    public void Improvement_ComparesBodyweightByReps_AndAssistedWithTheBody()
    {
        Workout(0, (PullUps, null, 8), (PullUps, null, 8), (PullUps, null, 8));
        var moreReps = Workout(1, (PullUps, null, 10), (PullUps, null, 10), (PullUps, null, 10));
        Workout(2, (PullUps, -50, 10));
        var lessHelp = Workout(3, (PullUps, -40, 10));
        var weighIn = new BodyWeightEntry { At = Monday, Weight = 200, WeightUnit = WeightUnit.Pounds };

        // (1 + 10/30) / (1 + 8/30), with or without a weigh-in.
        Assert.Equal(0.052632, Improvement(moreReps).Change!.Value, 6);
        Assert.Equal(0.052632, Improvement(moreReps, weighIn).Change!.Value, 6);

        // 160 lb lifted instead of 150. Without a weigh-in there's no telling.
        Assert.Equal(0.066667, Improvement(lessHelp, weighIn).Change!.Value, 6);
        Assert.Equal((null, 0), Improvement(lessHelp));
    }

    [Fact]
    public void Improvement_LiftsTheWeighInNearestBeforeTheWorkout_ElseTheFirstOneAfter()
    {
        var first = Workout(0, (PullUps, -50, 10));
        var beforeAny = Workout(1, (PullUps, -40, 10));
        Workout(3, (PullUps, -50, 10));
        var later = Workout(4, (PullUps, -40, 10));
        Workout(5, (PullUps, -50, 10));
        var last = Workout(6, (PullUps, -40, 10));
        BodyWeightEntry WeighIn(int days, double pounds) => new() { At = Monday.AddDays(days), Weight = pounds, WeightUnit = WeightUnit.Pounds };
        BodyWeightEntry[] weighIns = [WeighIn(40, 300), WeighIn(10, 250), WeighIn(27, 200), WeighIn(29, 210)];

        // Before any weigh-in, the first one after: 210 lb lifted instead of 200. The fourth week's workout lifts the 200 lb from
        // the day before, not the 250 or the later ones: 160 instead of 150. The sixth week's, the 300 lb from two days before.
        Assert.Equal(210 / 200.0 - 1, Improvement(beforeAny, weighIns).Change!.Value, 10);
        Assert.Equal(160 / 150.0 - 1, Improvement(later, weighIns).Change!.Value, 10);
        Assert.Equal(260 / 250.0 - 1, Improvement(last, weighIns).Change!.Value, 10);
        Assert.Equal(160 / 150.0 - 1, Assert.Single(Progress.Trend([first, later], sets, Exercises, weighIns)).Change, 10);
    }

    [Fact]
    public void Improvement_ComparesTimeByAverageSet_AndKilogramsWithPounds()
    {
        Workout(0, (Plank, null, 60), (Plank, null, 60), (Bench, 45.359, 10));
        sets[^1].WeightUnit = WeightUnit.Kilograms;
        var later = Workout(1, (Plank, null, 75), (Bench, 105, 10));

        // Plank 75 s against 60 (+25%), bench 105 lb against 100 (+5%): the geometric mean of 1.25 and 1.05.
        Assert.Equal(Math.Sqrt(1.25 * 1.05) - 1, Improvement(later).Change!.Value, 4);
        Assert.Equal(2, Improvement(later).Compared);
    }

    [Fact]
    public void Improvement_EvensOutUpsAndDownsOfTheSameRatio_AndLeavesOutFirstTimes()
    {
        Workout(0, (Bench, 100, 10), (Curls, 30, 10));
        var mixed = Workout(1, (Bench, 110, 10), (Curls, 30 / 1.1, 10), (Plank, null, 60));

        Assert.Equal(0, Improvement(mixed).Change!.Value, 10);
        Assert.Equal(2, Improvement(mixed).Compared);
    }

    [Fact]
    public void Improvement_ComparesWithTheLastEarlierWorkout_AndIsEmptyWithNothingBefore()
    {
        var first = Workout(0, (Bench, 100, 10));
        var up = Workout(1, (Bench, 110, 10));
        var down = Workout(2, (Bench, 100, 10));

        Assert.Equal((null, 0), Improvement(first));
        Assert.Equal(0.1, Improvement(up).Change!.Value, 6);
        Assert.Equal(-0.090909, Improvement(down).Change!.Value, 6);
    }

    [Fact]
    public void Trend_AveragesEachExercisesChangeSinceItsFirstTime()
    {
        Workout(0, (Bench, 100, 10), (Curls, 30, 10));
        var benchUp = Workout(1, (Bench, 110, 10));
        Workout(2, (Plank, null, 60));
        var curlsUp = Workout(3, (Curls, 33, 10));
        var curlsDown = Workout(4, (Curls, 30, 10));
        var open = new WorkoutSession { Id = 99, StartedAt = Monday.AddDays(40), Improvement = 0.5, ExercisesCompared = 1 };
        foreach (var session in sessions) (session.Improvement, session.ExercisesCompared) = Improvement(session);

        var trend = Progress.Trend([.. sessions, open], sets.Select(s => (s.SessionId, s.ExerciseId)));

        // Bench +10%, then curls +10% too: on average 10% better, not 21% for two good workouts. Curls back down: √1.1 − 1.
        Assert.Equal([benchUp, curlsUp, curlsDown], trend.Select(p => p.Session));
        Assert.Equal(0.10, trend[0].Total, 10);
        Assert.Equal(0.10, trend[1].Total, 10);
        Assert.Equal(Math.Sqrt(1.1) - 1, trend[2].Total, 10);
    }

    [Fact]
    public void Trend_OfSomeWorkouts_ComparesThemOnlyWithEachOther()
    {
        var monday = Workout(0, (Bench, 100, 10), (Curls, 30, 10));
        Workout(1, (Bench, 120, 10));
        var nextMonday = Workout(2, (Bench, 105, 10), (Curls, 30, 10));
        var open = new WorkoutSession { Id = 99, StartedAt = Monday.AddDays(30) };
        sets.Add(new WorkoutSet { SessionId = open.Id, ExerciseId = Bench, Order = 99, Weight = 200, Reps = 10 });

        var trend = Progress.Trend([monday, nextMonday, open], sets, Exercises, []);

        // Bench against last Monday's 100, not the 120 in between; curls the same. The open workout isn't done yet.
        Assert.Equal(Math.Sqrt(105 / 120.0) - 1, Improvement(nextMonday).Change!.Value, 10);
        var point = Assert.Single(trend);
        Assert.Equal(nextMonday, point.Session);
        Assert.Equal(Math.Sqrt(1.05) - 1, point.Change, 10);
        Assert.Equal(Math.Sqrt(1.05) - 1, point.Total, 10);
    }

    [Fact]
    public void Trend_OfOneExercisesSets_IsItsChangeSinceTheFirstTime()
    {
        Workout(0, (Bench, 100, 10), (Curls, 30, 10));
        var up = Workout(1, (Bench, 110, 10), (Curls, 20, 10));
        var down = Workout(2, (Bench, 99, 10));

        var trend = Progress.Trend(sessions, [.. sets.Where(s => s.ExerciseId == Bench)], Exercises, []);

        Assert.Equal([up, down], trend.Select(p => p.Session));
        Assert.Equal([0.1, -0.1], trend.Select(p => Math.Round(p.Change, 10)));
        Assert.Equal([0.1, -0.01], trend.Select(p => Math.Round(p.Total, 10)));
    }
}
