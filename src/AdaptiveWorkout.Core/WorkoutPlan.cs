namespace AdaptiveWorkout.Core;

/// <summary>What the big button offers next: a set of one exercise, or a superset's round, whose exercises start and stop together.</summary>
/// <param name="ExerciseIds">The exercise, or the superset's exercises in order.</param>
/// <param name="SetNumber">The set's number in this workout; for a superset, the round's.</param>
/// <param name="TargetSets">Sets (or rounds) planned for it, else as many as last time; empty when unknown.</param>
public sealed record PlanStep(IReadOnlyList<long> ExerciseIds, int SetNumber, int? TargetSets)
{
    /// <summary>The exercise, or the superset's first.</summary>
    public long ExerciseId => ExerciseIds[0];

    /// <summary>A superset's round: more than one exercise, back to back with no break between them.</summary>
    public bool IsRound => ExerciseIds.Count > 1;

    public bool Equals(PlanStep? other) => other is not null && ExerciseIds.SequenceEqual(other.ExerciseIds) && SetNumber == other.SetNumber && TargetSets == other.TargetSets;

    public override int GetHashCode() => HashCode.Combine(ExerciseId, SetNumber, TargetSets);
}

/// <summary>Simple mode's next step through a session.</summary>
public static class WorkoutPlan
{
    /// <summary>
    /// The next step, or null when nothing in the plan is left (or nothing is planned and nothing is done yet). The exercise or
    /// superset I'm on goes on until its planned sets, else as many sets as last time; without either it offers another set until
    /// I move on. Then the plan carries on after it: ones I went past, skipped or swapped out don't come back on their own.
    /// A superset's round is one step and counts as one set of each of its exercises.
    /// </summary>
    /// <param name="plan">The workout's plan: its own copy of the session template, changed as it goes. Empty when just tracking.</param>
    /// <param name="done">This session's finished sets, in order.</param>
    /// <param name="setsLastTime">Per exercise, how many sets I did the last time.</param>
    /// <param name="skipped">Exercises skipped in advanced mode.</param>
    public static PlanStep? Next(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> done, IReadOnlyDictionary<long, int> setsLastTime, IReadOnlyCollection<long> skipped)
    {
        if (plan.Count == 0)
        {
            if (done.Count == 0) return null;

            // Last time's count is shown only until I pass it; without a plan there's nothing to move on to.
            var lastExerciseId = done[^1].ExerciseId;
            var setNumber = done.Count(s => s.ExerciseId == lastExerciseId) + 1;
            int? lastTimeCount = setsLastTime.GetValueOrDefault(lastExerciseId) is var count && count >= setNumber ? count : null;
            return new PlanStep([lastExerciseId], setNumber, lastTimeCount);
        }

        var tally = new Tally(plan, done, setsLastTime);
        return tally.NextBlock(skipped) is int block ? tally.Step(block, skipped) : null;
    }

    /// <summary>Where <see cref="Next"/>'s step is in the plan: its block's index in <see cref="Blocks"/>; null when there's none.</summary>
    public static int? NextBlock(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> done, IReadOnlyDictionary<long, int> setsLastTime, IReadOnlyCollection<long> skipped) =>
        plan.Count == 0 ? null : new Tally(plan, done, setsLastTime).NextBlock(skipped);

    /// <summary>
    /// The plan's blocks that aren't done yet, in plan order, each with its next step: what the big button offers, and also ones
    /// I went past, which come back only when I pick them.
    /// </summary>
    public static IEnumerable<(List<TemplateExercise> Members, PlanStep Step)> Open(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> done, IReadOnlyDictionary<long, int> setsLastTime, IReadOnlyCollection<long> skipped)
    {
        var tally = new Tally(plan, done, setsLastTime);
        for (var block = 0; block < tally.Blocks.Count; block++)
        {
            if (tally.Offers(block, skipped)) yield return ([.. tally.Blocks[block].Where(e => !skipped.Contains(e.ExerciseId))], tally.Step(block, skipped));
        }
    }

    /// <summary>The sets each block of the plan counted, in the order of <see cref="Blocks"/>. Sets the plan doesn't have are in none.</summary>
    public static List<List<WorkoutSet>> Counted(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> done, IReadOnlyDictionary<long, int> setsLastTime) =>
        new Tally(plan, done, setsLastTime).Sets;

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

    // Where the finished sets went in the plan. Walking them in order, a set counts for the block I'm on while I stay with its
    // exercises; else for the first block with its exercise still to do from where I am, else anywhere, else its last block.
    // So an exercise done twice (in a superset, then in another one's place) counts afresh the second time. A set the plan
    // doesn't have (its exercise was swapped out or dropped) counts for nothing in it and leaves me where I was.
    private sealed class Tally
    {
        private readonly int?[] targets;
        private readonly int[][] counts;
        private readonly int[] lastSet;

        public Tally(IReadOnlyList<TemplateExercise> plan, IReadOnlyList<WorkoutSet> done, IReadOnlyDictionary<long, int> setsLastTime)
        {
            Blocks = WorkoutPlan.Blocks(plan);
            targets = [.. Blocks.Select(b => b[0].PlannedSets ?? (b.Max(e => setsLastTime.GetValueOrDefault(e.ExerciseId)) is var most && most > 0 ? most : null))];
            counts = [.. Blocks.Select(b => new int[b.Count])];
            lastSet = [.. Blocks.Select(_ => -1)];
            Sets = [.. Blocks.Select(_ => new List<WorkoutSet>())];
            for (var k = 0; k < done.Count; k++)
            {
                var exerciseId = done[k].ExerciseId;
                var holding = Enumerable.Range(0, Blocks.Count).Where(b => Blocks[b].Any(e => e.ExerciseId == exerciseId)).ToList();
                if (holding.Count == 0) continue;

                var at = holding.Contains(Position) ? Position
                    : holding.Where(b => b > Position && !Full(b)).Cast<int?>().FirstOrDefault() ?? holding.Where(b => !Full(b)).Cast<int?>().FirstOrDefault() ?? holding[^1];
                counts[at][Blocks[at].FindIndex(e => e.ExerciseId == exerciseId)]++;
                lastSet[at] = k;
                Sets[at].Add(done[k]);
                Position = at;
            }
        }

        public List<List<TemplateExercise>> Blocks { get; }

        public List<List<WorkoutSet>> Sets { get; }

        /// <summary>Where I am in the plan: the block of the last set the plan has, or -1 before any.</summary>
        public int Position { get; private set; } = -1;

        // The block I'm on while it's still to do, else the first one after it that is.
        public int? NextBlock(IReadOnlyCollection<long> skipped)
        {
            for (var block = Math.Max(Position, 0); block < Blocks.Count; block++)
            {
                if (Offers(block, skipped)) return block;
            }

            return null;
        }

        // Whether a block is still to do: short of its sets (a superset's rounds go by its furthest exercise), or without a count,
        // not yet moved on from: no block later in the plan had a set after its last. One whose exercises are all skipped isn't.
        public bool Offers(int block, IReadOnlyCollection<long> skipped)
        {
            var members = Members(block, skipped).ToList();
            if (members.Count == 0) return false;
            if (targets[block] is int target) return members.Max(i => counts[block][i]) < target;

            return lastSet[block] < 0 || !lastSet.Skip(block + 1).Any(k => k > lastSet[block]);
        }

        // The block's next step: all its exercises, together. So when one of a superset is swapped part way, the new one joins the
        // next round rather than starting over.
        public PlanStep Step(int block, IReadOnlyCollection<long> skipped)
        {
            var members = Members(block, skipped).ToList();
            return new PlanStep([.. members.Select(i => Blocks[block][i].ExerciseId)], members.Max(i => counts[block][i]) + 1, targets[block]);
        }

        private bool Full(int block) => targets[block] is int target && counts[block].Max() >= target;

        private IEnumerable<int> Members(int block, IReadOnlyCollection<long> skipped) =>
            Enumerable.Range(0, Blocks[block].Count).Where(i => !skipped.Contains(Blocks[block][i].ExerciseId));
    }
}
