using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class SetSuggestionTests
{
    private static WorkoutSet Set(double? weight, int reps, bool hitGoal = false) => new() { Weight = weight, Reps = reps, HitGoal = hitGoal };

    [Fact]
    public void For_RepeatsThePreviousSetThisSession()
    {
        var suggestion = SetSuggestion.For([Set(100, 10), Set(105, 8)], [Set(95, 10, hitGoal: true)], 5);

        Assert.Equal(new SetValues(105, WeightUnit.Pounds, 8, false), suggestion);
    }

    [Fact]
    public void For_FirstSetStartsFromTheFirstSetLastTime()
    {
        Assert.Equal(new SetValues(95, WeightUnit.Pounds, 10, false), SetSuggestion.For([], [Set(95, 10), Set(100, 8)], 5));
    }

    [Fact]
    public void For_AfterAHitGoal_SuggestsTheGoalWeightPlusTheStep()
    {
        var lastTime = new[] { Set(95, 10, hitGoal: true), Set(95, 10, hitGoal: true), Set(95, 10, hitGoal: true) };

        Assert.Equal(new SetValues(100, WeightUnit.Pounds, 10, true), SetSuggestion.For([], lastTime, 5));
    }

    [Fact]
    public void For_AfterAHitGoal_AssistedWeightMovesTowardLessAssistance()
    {
        Assert.Equal(new SetValues(-35, WeightUnit.Pounds, 8, true), SetSuggestion.For([], [Set(-40, 8, hitGoal: true)], 5));
    }

    [Fact]
    public void For_AfterAHitGoal_BodyweightStaysBodyweight()
    {
        Assert.Equal(new SetValues(null, WeightUnit.Pounds, 12, false), SetSuggestion.For([], [Set(null, 12, hitGoal: true)], 5));
    }

    [Fact]
    public void For_IsEmptyForAnExerciseIHaveNeverDone()
    {
        Assert.Null(SetSuggestion.For([], [], 5));
    }
}
