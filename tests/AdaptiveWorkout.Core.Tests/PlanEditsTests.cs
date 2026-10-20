using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class PlanEditsTests
{
    private const long Bench = 1, Incline = 2, Fly = 3, PullUps = 4, Dips = 5, Pulldown = 6, Skulls = 7, Crunches = 8, Plank = 9;

    private static readonly Dictionary<long, int> NoHistory = [];

    private static TemplateExercise Entry(long exerciseId, int order, int? sets = null, int? superset = null) =>
        new() { ExerciseId = exerciseId, Order = order, PlannedSets = sets, SupersetGroup = superset };

    private static List<WorkoutSet> Done(params long[] exerciseIds) => [.. exerciseIds.Select((id, i) => new WorkoutSet { ExerciseId = id, Order = i + 1 })];

    private static string Show(IEnumerable<TemplateExercise> plan) =>
        string.Join(" ", plan.Select(e => $"{e.ExerciseId}{(e.PlannedSets is int n ? $"x{n}" : "")}{(e.SupersetGroup is int g ? $"s{g}" : "")}"));

    [Fact]
    public void DoFirst_BenchTaken_InclineNowAndBenchRightAfter()
    {
        TemplateExercise[] plan = [Entry(Bench, 0, sets: 3), Entry(Incline, 1, sets: 3), Entry(Fly, 2, sets: 3)];

        var changed = PlanEdits.DoFirst(plan, Incline, Bench);

        Assert.Equal("2x3 1x3 3x3", Show(changed));
        Assert.Equal(new PlanStep([Incline], 1, 3), WorkoutPlan.Next(changed, [], NoHistory, []));
        Assert.Equal(new PlanStep([Bench], 1, 3), WorkoutPlan.Next(changed, Done(Incline, Incline, Incline), NoHistory, []));
    }

    [Fact]
    public void DoFirst_PulldownTaken_SkullCrushersFirstThenBackToPulldown()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1), Entry(Pulldown, 2, sets: 3), Entry(Skulls, 3, sets: 3)];
        var done = Done(PullUps, Dips, PullUps, Dips, PullUps, Dips);

        var changed = PlanEdits.DoFirst(plan, Skulls, Pulldown);

        Assert.Equal("4x3s1 5s1 7x3 6x3", Show(changed));
        Assert.Equal(Skulls, WorkoutPlan.Next(changed, done, NoHistory, [])?.ExerciseId);
        Assert.Equal(Pulldown, WorkoutPlan.Next(changed, [.. done, .. Done(Skulls, Skulls, Skulls)], NoHistory, [])?.ExerciseId);
    }

    [Fact]
    public void DoFirst_AfterStartingTheOtherOne_OpenEndedExerciseStillGetsItsSets()
    {
        TemplateExercise[] plan = [Entry(Fly, 0, sets: 2), Entry(Crunches, 1), Entry(Skulls, 2)];
        var done = Done(Fly, Fly, Crunches);

        var changed = PlanEdits.DoFirst(plan, Skulls, Crunches);

        Assert.Equal(new PlanStep([Skulls], 2, null), WorkoutPlan.Next(changed, [.. done, .. Done(Skulls)], NoHistory, []));
        Assert.Equal(new PlanStep([Crunches], 3, null), WorkoutPlan.Next(changed, [.. done, .. Done(Skulls, Crunches)], NoHistory, []));
    }

    [Fact]
    public void DoFirst_PartWayThrough_TheOtherOneNowAndTheRestRightAfter()
    {
        TemplateExercise[] plan = [Entry(Bench, 0, sets: 3), Entry(PullUps, 1, sets: 3, superset: 2), Entry(Dips, 2, superset: 2), Entry(Fly, 3, sets: 3)];
        var done = Done(Bench);

        var changed = PlanEdits.DoFirst(plan, Dips, WorkoutPlan.NextBlock(plan, done, NoHistory, [])!.Value, 1, 2);

        Assert.Equal("1x1 5x3s2 4s2 1x2 3x3", Show(changed));
        Assert.Equal(new PlanStep([Dips, PullUps], 1, 3), WorkoutPlan.Next(changed, done, NoHistory, []));
        Assert.Equal(new PlanStep([Bench], 1, 2), WorkoutPlan.Next(changed, [.. done, .. Done(Dips, PullUps, Dips, PullUps, Dips, PullUps)], NoHistory, []));
        Assert.Equal(new PlanStep([Fly], 1, 3), WorkoutPlan.Next(changed, [.. done, .. Done(Dips, PullUps, Dips, PullUps, Dips, PullUps, Bench, Bench)], NoHistory, []));
    }

    [Fact]
    public void DoFirst_MovesAWholeSupersetLedByTheHalfPicked()
    {
        TemplateExercise[] plan = [Entry(Pulldown, 0, sets: 3), Entry(PullUps, 1, sets: 3, superset: 2), Entry(Dips, 2, superset: 2)];

        var changed = PlanEdits.DoFirst(plan, Dips, Pulldown);

        Assert.Equal("5x3s1 4s1 6x3", Show(changed));
        Assert.Equal(new PlanStep([Dips, PullUps], 1, 3), WorkoutPlan.Next(changed, [], NoHistory, []));
    }

    [Fact]
    public void DoFirst_InsideASuperset_ThatHalfStartsTheRounds()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1)];

        var changed = PlanEdits.DoFirst(plan, Dips, PullUps);

        Assert.Equal("5x3s1 4s1", Show(changed));
        Assert.Equal(new PlanStep([Dips, PullUps], 1, 3), WorkoutPlan.Next(changed, [], NoHistory, []));
    }

    [Fact]
    public void Swap_KeepsTheSpotTheSetsAndTheSuperset()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1), Entry(Pulldown, 2, sets: 3)];

        Assert.Equal("4x3s1 9s1 6x3", Show(PlanEdits.Swap(plan, Dips, Plank)));
        Assert.Equal("9x3s1 5s1 6x3", Show(PlanEdits.Swap(plan, PullUps, Plank)));
    }

    [Fact]
    public void Drop_NoDipStation_PullUpsKeepTheirRoundsAlone()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1), Entry(Pulldown, 2, sets: 3)];

        var withoutDips = PlanEdits.Drop(plan, Dips);
        var withoutPullUps = PlanEdits.Drop(plan, PullUps);

        Assert.Equal("4x3 6x3", Show(withoutDips));
        Assert.Equal(new PlanStep([PullUps], 2, 3), WorkoutPlan.Next(withoutDips, Done(PullUps), NoHistory, []));
        Assert.Equal("5x3 6x3", Show(withoutPullUps));
    }

    [Fact]
    public void InsertAfter_AnExercisePickedInsteadOfTheNextOne_TakesItsPlace()
    {
        TemplateExercise[] plan = [Entry(Fly, 0, sets: 3), Entry(Pulldown, 1, sets: 3), Entry(Dips, 2, sets: 3)];
        var done = Done(Fly, Fly, Fly);
        var lastTime = new Dictionary<long, int> { [Skulls] = 2 };

        var changed = PlanEdits.InsertAfter(plan, Skulls, WorkoutPlan.NextBlock(plan, done, NoHistory, []));

        Assert.Equal("3x3 6x3 7 5x3", Show(changed));
        Assert.Equal(new PlanStep([Skulls], 2, null), WorkoutPlan.Next(changed, [.. done, .. Done(Skulls)], NoHistory, []));
        Assert.Equal(new PlanStep([Dips], 1, 3), WorkoutPlan.Next(changed, [.. done, .. Done(Skulls, Skulls)], lastTime, []));
        Assert.Equal("3x3 6x3 5x3 7", Show(PlanEdits.InsertAfter(plan, Skulls, null)));
    }

    [Fact]
    public void Append_AnotherSessionAfterwards_KeepsItsSupersetsApart()
    {
        TemplateExercise[] plan = [Entry(PullUps, 0, sets: 3, superset: 1), Entry(Dips, 1, superset: 1)];
        TemplateExercise[] absDay = [Entry(Crunches, 0, sets: 3, superset: 1), Entry(Plank, 1, superset: 1), Entry(Fly, 2)];

        var changed = PlanEdits.Append(plan, absDay);

        Assert.Equal("4x3s1 5s1 8x3s2 9s2 3", Show(changed));
        Assert.Equal(Crunches, WorkoutPlan.Next(changed, Done(PullUps, Dips, PullUps, Dips, PullUps, Dips), NoHistory, [])?.ExerciseId);
    }

    [Fact]
    public void Open_ListsWhatsStillToDo()
    {
        TemplateExercise[] plan = [Entry(Bench, 0, sets: 2), Entry(PullUps, 1, sets: 3, superset: 2), Entry(Dips, 2, superset: 2), Entry(Fly, 3, sets: 3)];

        var open = WorkoutPlan.Open(plan, Done(Bench, Bench, PullUps), NoHistory, []).Select(o => string.Join("+", o.Members.Select(m => m.ExerciseId)));

        Assert.Equal(["4+5", "3"], open);
    }
}
