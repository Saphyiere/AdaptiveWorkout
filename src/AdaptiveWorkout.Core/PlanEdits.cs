namespace AdaptiveWorkout.Core;

/// <summary>
/// Changes to one workout's own plan while it's going, e.g. when a machine is taken. The session template it came from never changes.
/// Each edit returns a new plan, numbered from 0, with one superset group number per superset.
/// </summary>
public static class PlanEdits
{
    /// <summary>A template's exercises as a workout's own plan.</summary>
    public static List<TemplateExercise> Copy(IReadOnlyList<TemplateExercise> entries) => Build(WorkoutPlan.Blocks(entries));

    /// <summary>
    /// Moves an exercise, with its whole superset, to just before another one, which waits until it's done.
    /// In a superset, the exercise picked starts the rounds. Unknown exercises leave the plan as it is.
    /// </summary>
    public static List<TemplateExercise> DoFirst(IReadOnlyList<TemplateExercise> plan, long exerciseId, long beforeExerciseId)
    {
        var blocks = WorkoutPlan.Blocks(plan);
        var moving = blocks.Find(b => b.Any(e => e.ExerciseId == exerciseId));
        var waiting = blocks.Find(b => b.Any(e => e.ExerciseId == beforeExerciseId));
        if (moving is null || waiting is null) return Build(blocks);

        var rounds = moving[0].PlannedSets;
        var picked = moving.Find(e => e.ExerciseId == exerciseId)!;
        moving.Remove(picked);
        moving.Insert(0, picked);
        if (moving != waiting)
        {
            blocks.Remove(moving);
            blocks.Insert(blocks.IndexOf(waiting), moving);
        }

        return Build(blocks, (moving, rounds));
    }

    /// <summary>Puts another exercise in one's place: same spot, same sets, same superset.</summary>
    public static List<TemplateExercise> Swap(IReadOnlyList<TemplateExercise> plan, long exerciseId, long newExerciseId) =>
        Build(WorkoutPlan.Blocks(plan).Select(b => b.Select(e => e.ExerciseId == exerciseId ? With(e, newExerciseId) : e).ToList()));

    /// <summary>Leaves an exercise out. The rest of its superset keeps the rounds.</summary>
    public static List<TemplateExercise> Drop(IReadOnlyList<TemplateExercise> plan, long exerciseId)
    {
        var blocks = WorkoutPlan.Blocks(plan);
        var block = blocks.Find(b => b.Any(e => e.ExerciseId == exerciseId));
        if (block is null) return Build(blocks);

        var rounds = block[0].PlannedSets;
        block.RemoveAll(e => e.ExerciseId == exerciseId);
        return Build(blocks, (block, rounds));
    }

    /// <summary>Adds exercises after everything else, e.g. a whole other session. Its supersets stay their own.</summary>
    public static List<TemplateExercise> Append(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<TemplateExercise> entries) =>
        Build([.. WorkoutPlan.Blocks(plan), .. WorkoutPlan.Blocks(entries)]);

    // Numbers the entries in order. A superset's planned rounds live on its first entry; `carry` gives a reshuffled block its rounds back.
    private static List<TemplateExercise> Build(IEnumerable<List<TemplateExercise>> blocks, (List<TemplateExercise> Block, int? Rounds)? carry = null)
    {
        var plan = new List<TemplateExercise>();
        var group = 0;
        foreach (var block in blocks.Where(b => b.Count > 0))
        {
            group++;
            for (var i = 0; i < block.Count; i++)
            {
                var plannedSets = i > 0 ? null : block == carry?.Block ? carry.Value.Rounds : block[0].PlannedSets;
                plan.Add(new TemplateExercise { ExerciseId = block[i].ExerciseId, Order = plan.Count, PlannedSets = plannedSets, SupersetGroup = block.Count > 1 ? group : null });
            }
        }

        return plan;
    }

    private static TemplateExercise With(TemplateExercise entry, long exerciseId) =>
        new() { ExerciseId = exerciseId, Order = entry.Order, PlannedSets = entry.PlannedSets, SupersetGroup = entry.SupersetGroup };
}
