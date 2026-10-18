using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class SetSummaryTests
{
    private static WorkoutSet[] Sets(params (double? Weight, int Reps)[] sets) => [.. sets.Select(s => new WorkoutSet { Weight = s.Weight, Reps = s.Reps })];

    [Fact]
    public void Format_GroupsConsecutiveSetsAtTheSameWeight()
    {
        Assert.Equal("3 × 10 at 95", SetSummary.Format(Sets((95, 10), (95, 10), (95, 10)), ExerciseMeasure.Weight));
        Assert.Equal("10, 10, 8 at 95", SetSummary.Format(Sets((95, 10), (95, 10), (95, 8)), ExerciseMeasure.Weight));
        Assert.Equal("8 at 95 · 2 × 10 at 90", SetSummary.Format(Sets((95, 8), (90, 10), (90, 10)), ExerciseMeasure.Weight));
    }

    [Fact]
    public void Format_ShowsBodyweightAndAssistedWeights()
    {
        Assert.Equal("12, 12, 10 at bodyweight", SetSummary.Format(Sets((null, 12), (null, 12), (null, 10)), ExerciseMeasure.Weight));
        Assert.Equal("2 × 8 at -40 · 6 at -32.5", SetSummary.Format(Sets((-40, 8), (-40, 8), (-32.5, 6)), ExerciseMeasure.Weight));
    }

    [Fact]
    public void Format_ListsDurationsForTimeExercises()
    {
        var sets = new[] { new WorkoutSet { DurationSeconds = 300 }, new WorkoutSet { DurationSeconds = 270 }, new WorkoutSet { DurationSeconds = 3725 } };

        Assert.Equal("5:00, 4:30, 1:02:05", SetSummary.Format(sets, ExerciseMeasure.Time));
    }

    [Fact]
    public void PercentText_SignsChangesAndRoundsTinyOnesToZero()
    {
        Assert.Equal("+3.1%", PercentText.Format(0.0312));
        Assert.Equal("−1.2%", PercentText.Format(-0.0118));
        Assert.Equal("+12%", PercentText.Format(0.12));
        Assert.Equal("0%", PercentText.Format(0.0004));
        Assert.Equal("0%", PercentText.Format(-0.0004));
    }
}
