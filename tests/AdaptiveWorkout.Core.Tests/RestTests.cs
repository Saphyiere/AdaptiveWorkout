using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class RestTests
{
    private const long Rows = 1, Pulldown = 2, PullUps = 3, Dips = 4;

    private static readonly DateTimeOffset Start = new(2026, 10, 16, 18, 0, 0, TimeSpan.FromHours(-4));

    // Each set lasts 45 seconds; RestAfter is the rest before the next set, in seconds.
    private static List<WorkoutSet> Sets(params (long ExerciseId, int RestAfter)[] sets)
    {
        var at = Start;
        var result = new List<WorkoutSet>();
        foreach (var (exerciseId, restAfter) in sets)
        {
            result.Add(new WorkoutSet { ExerciseId = exerciseId, Order = result.Count + 1, StartedAt = at, StoppedAt = at.AddSeconds(45) });
            at = at.AddSeconds(45 + restAfter);
        }

        return result;
    }

    [Fact]
    public void Typical_TakesTheMiddleRestRoundedTo15Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), Rest.Typical(Sets((Rows, 80), (Rows, 95), (Rows, 200), (Pulldown, 0)), Rows));
        Assert.Equal(TimeSpan.FromSeconds(120), Rest.Typical(Sets((Rows, 110), (Rows, 126), (Rows, 0)), Rows));
    }

    [Fact]
    public void Typical_OnlyCountsRestsAfterThisExercise_AndNotLongBreaks()
    {
        var sets = Sets((Rows, 60), (Rows, 30 * 60), (Pulldown, 150), (Pulldown, 150), (Rows, 0));

        Assert.Equal(TimeSpan.FromSeconds(60), Rest.Typical(sets, Rows));
        Assert.Equal(TimeSpan.FromSeconds(150), Rest.Typical(sets, Pulldown));
    }

    [Fact]
    public void Typical_ASupersetRestsAfterEachRound_WhoseSetsStartAndStopTogether()
    {
        // Three rounds of a minute each, every 150 seconds: a 90-second rest after each, kept with the round's last exercise.
        var sets = new List<WorkoutSet>();
        for (var round = 0; round < 3; round++)
        {
            foreach (var exerciseId in new[] { PullUps, Dips }) sets.Add(new WorkoutSet { ExerciseId = exerciseId, Order = sets.Count + 1, StartedAt = Start.AddSeconds(round * 150), StoppedAt = Start.AddSeconds(round * 150 + 60) });
        }

        Assert.Equal(TimeSpan.FromSeconds(90), Rest.Typical(sets, Dips));
    }

    [Fact]
    public void Typical_StaysBetween30SecondsAnd5Minutes_AndIsEmptyWithNothingToGoOn()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), Rest.Typical(Sets((Rows, 5), (Rows, 0)), Rows));
        Assert.Equal(TimeSpan.FromMinutes(5), Rest.Typical(Sets((Rows, 9 * 60), (Rows, 0)), Rows));
        Assert.Null(Rest.Typical(Sets((Rows, 60)), Rows));
    }
}
