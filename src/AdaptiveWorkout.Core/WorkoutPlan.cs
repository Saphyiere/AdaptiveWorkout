namespace AdaptiveWorkout.Core;

/// <summary>What the big button offers next.</summary>
/// <param name="SetNumber">This exercise's set number in the session.</param>
/// <param name="TargetSets">Sets (or superset rounds) planned for it; empty when unknown.</param>
/// <param name="SupersetRound">The round, when the step is part of a superset.</param>
/// <param name="AfterBreak">False when the step continues a superset round, which has no break before it.</param>
public sealed record PlanStep(long ExerciseId, int SetNumber, int? TargetSets, int? SupersetRound, bool AfterBreak);

/// <summary>Simple mode's next step through a session.</summary>
public static class WorkoutPlan
{
    /// <summary>
    /// The next step, or null when every planned exercise is done (or nothing is planned and nothing is done yet).
    /// An exercise moves on after its planned sets, else after as many sets as last time; without either it offers another set
    /// until I move past it. Superset members alternate, and a round counts as one set of each.
    /// </summary>
    /// <param name="plan">The session template's exercises; empty when just tracking.</param>
    /// <param name="done">This session's finished sets, in order.</param>
    /// <param name="setsLastTime">Per exercise, how many sets I did the last time.</param>
    /// <param name="skipped">Exercises skipped in advanced mode.</param>
    public static PlanStep? Next(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> done, IReadOnlyDictionary<long, int> setsLastTime, IReadOnlyCollection<long> skipped)
    {
        var doneCounts = done.GroupBy(s => s.ExerciseId).ToDictionary(g => g.Key, g => g.Count());

        if (plan.Count == 0)
        {
            if (done.Count == 0) return null;

            // Last time's count is shown only until I pass it; without a plan there's nothing to move on to.
            var lastExerciseId = done[^1].ExerciseId;
            var setNumber = doneCounts[lastExerciseId] + 1;
            int? lastTimeCount = setsLastTime.GetValueOrDefault(lastExerciseId) is var count && count >= setNumber ? count : null;
            return new PlanStep(lastExerciseId, setNumber, lastTimeCount, null, true);
        }

        var blocks = Blocks(plan);
        var furthestStarted = blocks.FindLastIndex(b => b.Any(e => doneCounts.ContainsKey(e.ExerciseId)));
        for (var i = 0; i < blocks.Count; i++)
        {
            var members = blocks[i].Where(e => !skipped.Contains(e.ExerciseId)).ToList();
            if (members.Count == 0) continue;

            var counts = members.Select(e => doneCounts.GetValueOrDefault(e.ExerciseId)).ToList();
            var mostLastTime = members.Max(e => setsLastTime.GetValueOrDefault(e.ExerciseId));
            int? target = blocks[i][0].PlannedSets ?? (mostLastTime > 0 ? mostLastTime : null);
            if (target is int planned && counts.All(c => c >= planned)) continue;

            // Without a count, an exercise is done once I've moved on to a later one.
            if (target is null && counts.Any(c => c > 0) && i < furthestStarted) continue;

            var fewest = counts.Min();
            var nextIndex = counts.IndexOf(fewest);
            var isSuperset = blocks[i].Count > 1;
            return new PlanStep(members[nextIndex].ExerciseId, fewest + 1, target, isSuperset ? fewest + 1 : null, !isSuperset || nextIndex == 0);
        }

        return null;
    }

    /// <summary>The plan in order, one block per exercise or superset.</summary>
    public static List<List<TemplateExercise>> Blocks(IReadOnlyList<TemplateExercise> plan)
    {
        var blocks = new List<List<TemplateExercise>>();
        foreach (var entry in plan.OrderBy(e => e.Order))
        {
            var joinsSuperset = entry.SupersetGroup is not null && blocks.Count > 0 && blocks[^1][0].SupersetGroup == entry.SupersetGroup;
            if (joinsSuperset) blocks[^1].Add(entry);
            else blocks.Add([entry]);
        }

        return blocks;
    }
}
