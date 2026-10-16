using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class WorkoutPlanTests
{
    private const long Rows = 1, PullUps = 2, Dips = 3, Pulldown = 4;

    private static readonly Dictionary<long, int> NoHistory = [];

    private static TemplateExercise Entry(long exerciseId, int order, int? sets = null, int? superset = null) =>
        new() { ExerciseId = exerciseId, Order = order, PlannedSets = sets, SupersetGroup = superset };

    private static List<WorkoutSet> Done(params long[] exerciseIds) => [.. exerciseIds.Select((id, i) => new WorkoutSet { ExerciseId = id, Order = i + 1 })];

    [Fact]
    public void Next_MovesOnAfterThePlannedSets()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 2)];

        Assert.Equal(new PlanStep(Rows, 3, 3, null, true), WorkoutPlan.Next(plan, Done(Rows, Rows), NoHistory, []));
        Assert.Equal(new PlanStep(Pulldown, 1, 2, null, true), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, []));
        Assert.Null(WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, Pulldown, Pulldown), NoHistory, []));
    }

    [Fact]
    public void Next_UsesLastTimesSetCountWhenNothingIsPlanned()
    {
        TemplateExercise[] plan = [Entry(Pulldown, 0), Entry(Rows, 1, sets: 3)];
        var lastTime = new Dictionary<long, int> { [Pulldown] = 4 };

        Assert.Equal(new PlanStep(Pulldown, 4, 4, null, true), WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Pulldown), lastTime, []));
        Assert.Equal(Rows, WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Pulldown, Pulldown), lastTime, [])?.ExerciseId);
    }

    [Fact]
    public void Next_WithoutAnyCount_OffersAnotherSetUntilIMoveOn()
    {
        TemplateExercise[] plan = [Entry(Pulldown, 0), Entry(Rows, 1, sets: 3)];

        Assert.Equal(new PlanStep(Pulldown, 6, null, null, true), WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Pulldown, Pulldown, Pulldown), NoHistory, []));
        Assert.Equal(new PlanStep(Rows, 2, 3, null, true), WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Rows), NoHistory, []));
    }

    [Fact]
    public void Next_ComesBackToAnExerciseIWentAroundBecauseTheMachineWasTaken()
    {
        TemplateExercise[] counted = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3), Entry(Dips, 2, sets: 3)];
        TemplateExercise[] uncounted = [Entry(Pulldown, 0), Entry(Rows, 1, sets: 3)];

        Assert.Equal(new PlanStep(Pulldown, 1, 3, null, true), WorkoutPlan.Next(counted, Done(Rows, Rows, Rows, Dips, Dips, Dips), NoHistory, []));
        Assert.Equal(new PlanStep(Pulldown, 1, null, null, true), WorkoutPlan.Next(uncounted, Done(Rows, Rows, Rows), NoHistory, []));
    }

    [Fact]
    public void Next_AlternatesASupersetWithABreakOnlyAfterEachRound()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(PullUps, 1, sets: 2, superset: 1), Entry(Dips, 2, superset: 1), Entry(Pulldown, 3, sets: 3)];

        Assert.Equal(new PlanStep(PullUps, 1, 2, 1, true), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, []));
        Assert.Equal(new PlanStep(Dips, 1, 2, 1, false), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, PullUps), NoHistory, []));
        Assert.Equal(new PlanStep(PullUps, 2, 2, 2, true), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, PullUps, Dips), NoHistory, []));
        Assert.Equal(Pulldown, WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, PullUps, Dips, PullUps, Dips), NoHistory, [])?.ExerciseId);
    }

    [Fact]
    public void Next_PassesOverSkippedExercises()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3), Entry(Dips, 2, sets: 3)];

        Assert.Equal(Dips, WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, [Pulldown])?.ExerciseId);
        Assert.Null(WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, [Pulldown, Dips]));
    }

    [Fact]
    public void Next_WhenJustTracking_OffersAnotherSetOfTheLastExercise()
    {
        var lastTime = new Dictionary<long, int> { [Dips] = 3 };

        Assert.Null(WorkoutPlan.Next([], [], lastTime, []));
        Assert.Equal(new PlanStep(Dips, 2, 3, null, true), WorkoutPlan.Next([], Done(Rows, Dips), lastTime, []));
    }
}
