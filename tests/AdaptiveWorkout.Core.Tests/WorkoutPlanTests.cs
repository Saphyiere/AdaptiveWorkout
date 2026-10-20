using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class WorkoutPlanTests
{
    private const long Rows = 1, PullUps = 2, Dips = 3, Pulldown = 4, Skulls = 5, Curls = 6;

    private static readonly Dictionary<long, int> NoHistory = [];

    private static TemplateExercise Entry(long exerciseId, int order, int? sets = null, int? superset = null) =>
        new() { ExerciseId = exerciseId, Order = order, PlannedSets = sets, SupersetGroup = superset };

    private static List<WorkoutSet> Done(params long[] exerciseIds) => [.. exerciseIds.Select((id, i) => new WorkoutSet { ExerciseId = id, Order = i + 1 })];

    [Fact]
    public void Next_MovesOnAfterThePlannedSets()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 2)];

        Assert.Equal(new PlanStep([Rows], 3, 3), WorkoutPlan.Next(plan, Done(Rows, Rows), NoHistory, []));
        Assert.Equal(new PlanStep([Pulldown], 1, 2), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, []));
        Assert.Null(WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, Pulldown, Pulldown), NoHistory, []));
    }

    [Fact]
    public void Next_UsesLastTimesSetCountWhenNothingIsPlanned()
    {
        TemplateExercise[] plan = [Entry(Pulldown, 0), Entry(Rows, 1, sets: 3)];
        var lastTime = new Dictionary<long, int> { [Pulldown] = 4 };

        Assert.Equal(new PlanStep([Pulldown], 4, 4), WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Pulldown), lastTime, []));
        Assert.Equal(Rows, WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Pulldown, Pulldown), lastTime, [])?.ExerciseId);
    }

    [Fact]
    public void Next_WithoutAnyCount_OffersAnotherSetUntilIMoveOn()
    {
        TemplateExercise[] plan = [Entry(Pulldown, 0), Entry(Rows, 1, sets: 3)];

        Assert.Equal(new PlanStep([Pulldown], 6, null), WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Pulldown, Pulldown, Pulldown), NoHistory, []));
        Assert.Equal(new PlanStep([Rows], 2, 3), WorkoutPlan.Next(plan, Done(Pulldown, Pulldown, Rows), NoHistory, []));
    }

    [Fact]
    public void Next_KeepsGoingWithTheExerciseImOn_AndNeverGoesBackToOnesIWentPast()
    {
        TemplateExercise[] counted = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3), Entry(Skulls, 2, sets: 3), Entry(Dips, 3, sets: 3)];
        TemplateExercise[] uncounted = [Entry(Pulldown, 0), Entry(Rows, 1, sets: 3)];

        Assert.Equal(new PlanStep([Skulls], 2, 3), WorkoutPlan.Next(counted, Done(Rows, Rows, Rows, Skulls), NoHistory, []));
        Assert.Equal(new PlanStep([Dips], 1, 3), WorkoutPlan.Next(counted, Done(Rows, Rows, Rows, Skulls, Skulls, Skulls), NoHistory, []));
        Assert.Null(WorkoutPlan.Next(counted, Done(Rows, Rows, Rows, Skulls, Skulls, Skulls, Dips, Dips, Dips), NoHistory, []));
        Assert.Equal(new PlanStep([Rows], 2, 3), WorkoutPlan.Next(uncounted, Done(Rows), NoHistory, []));
        Assert.Null(WorkoutPlan.Next(uncounted, Done(Rows, Rows, Rows), NoHistory, []));
    }

    [Fact]
    public void Next_ASwappedInExerciseAlreadyDoneTodayCountsAfresh()
    {
        TemplateExercise[] plan = [Entry(Skulls, 0, sets: 3, superset: 1), Entry(Rows, 1, superset: 1), Entry(PullUps, 2, sets: 3, superset: 2), Entry(Dips, 3, superset: 2), Entry(Pulldown, 4, sets: 3)];
        var done = Done(Skulls, Rows, Skulls, Rows, Skulls, Rows, PullUps, Dips, PullUps, Dips, PullUps, Dips);

        var swapped = PlanEdits.Swap(plan, Pulldown, Skulls);

        Assert.Equal(new PlanStep([Skulls], 1, 3), WorkoutPlan.Next(swapped, done, NoHistory, []));
        Assert.Equal(new PlanStep([Skulls], 2, 3), WorkoutPlan.Next(swapped, [.. done, .. Done(Skulls)], NoHistory, []));
        Assert.Null(WorkoutPlan.Next(swapped, [.. done, .. Done(Skulls, Skulls, Skulls)], NoHistory, []));
    }

    [Fact]
    public void Next_SetsOfAnExerciseNoLongerPlannedDontCarryOn()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3)];

        Assert.Equal(new PlanStep([Pulldown], 1, 3), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, Curls), NoHistory, []));
    }

    [Fact]
    public void Next_ASupersetRoundIsOneStep()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(PullUps, 1, sets: 2, superset: 1), Entry(Dips, 2, superset: 1), Entry(Pulldown, 3, sets: 3)];

        Assert.Equal(new PlanStep([PullUps, Dips], 1, 2), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, []));
        Assert.Equal(new PlanStep([PullUps, Dips], 2, 2), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, PullUps, Dips), NoHistory, []));
        Assert.Equal(new PlanStep([Pulldown], 1, 3), WorkoutPlan.Next(plan, Done(Rows, Rows, Rows, PullUps, Dips, PullUps, Dips), NoHistory, []));
    }

    [Fact]
    public void Next_OneOfASupersetSwappedPartWay_JoinsTheNextRound()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1), Entry(Rows, 2, sets: 3)];

        var swapped = PlanEdits.Swap(plan, Dips, Skulls);

        Assert.Equal(new PlanStep([PullUps, Skulls], 2, 3), WorkoutPlan.Next(swapped, Done(PullUps, Dips), NoHistory, []));
        Assert.Equal(new PlanStep([Rows], 1, 3), WorkoutPlan.Next(swapped, Done(PullUps, Dips, PullUps, Skulls, PullUps, Skulls), NoHistory, []));
    }

    [Fact]
    public void Next_PassesOverSkippedExercises()
    {
        TemplateExercise[] plan = [Entry(Rows, 0, sets: 3), Entry(Pulldown, 1, sets: 3), Entry(Dips, 2, sets: 3)];
        TemplateExercise[] superset = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1)];

        Assert.Equal(Dips, WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, [Pulldown])?.ExerciseId);
        Assert.Null(WorkoutPlan.Next(plan, Done(Rows, Rows, Rows), NoHistory, [Pulldown, Dips]));
        Assert.Equal(new PlanStep([PullUps], 1, 3), WorkoutPlan.Next(superset, [], NoHistory, [Dips]));
    }

    [Fact]
    public void Next_WhenJustTracking_OffersAnotherSetOfTheLastExercise()
    {
        var lastTime = new Dictionary<long, int> { [Dips] = 3 };

        Assert.Null(WorkoutPlan.Next([], [], lastTime, []));
        Assert.Equal(new PlanStep([Dips], 2, 3), WorkoutPlan.Next([], Done(Rows, Dips), lastTime, []));
        Assert.Equal(new PlanStep([Dips], 4, null), WorkoutPlan.Next([], Done(Dips, Dips, Dips), lastTime, []));
    }
}
