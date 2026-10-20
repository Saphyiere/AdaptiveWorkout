using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.UI.Workouts;

/// <summary>The open workout, kept in memory so every tap updates the screen at once. <see cref="WorkoutFlow"/> changes it and saves in the background.</summary>
public sealed class WorkoutState
{
    private readonly Dictionary<long, IReadOnlyList<PastSession>> historyCache = [];
    private Dictionary<long, int> setsLastTime = [];

    public required WorkoutSession Session { get; init; }
    public SessionTemplate? Template { get; init; }

    /// <summary>This workout's own plan: a copy of the template's exercises, changed as it goes. Empty when just tracking.</summary>
    public required List<TemplateExercise> Plan { get; set; }

    public required Dictionary<long, Exercise> Exercises { get; init; }

    /// <summary>This session's sets, in order. The last one may still be running.</summary>
    public required List<WorkoutSet> Sets { get; init; }

    /// <summary>Every other session and set, for history and suggestions.</summary>
    public required IReadOnlyList<WorkoutSession> PastSessions { get; init; }

    public required IReadOnlyList<WorkoutSet> PastSets { get; init; }
    public required double HitGoalStep { get; init; }

    public HashSet<long> Skipped { get; } = [];

    /// <summary>Sets up to this order were entered at an earlier rest, or before the workout was reopened.</summary>
    public int LoggedThrough { get; set; }

    /// <summary>Sets saved on the current rest screen, by order.</summary>
    public HashSet<int> Confirmed { get; } = [];

    /// <summary>
    /// What the rest screen asks for: weight sets stopped since the last rest and not saved yet. A superset round's sets
    /// all wait for the rest after the round. Time sets need nothing; the stopwatch was the entry.
    /// </summary>
    public List<WorkoutSet> ToConfirm => [.. Sets.Where(s => s.StoppedAt is not null && s.Order > LoggedThrough && !Confirmed.Contains(s.Order) && Exercises[s.ExerciseId].Measure == ExerciseMeasure.Weight)];

    public string Name => Template?.Name ?? "Workout";

    /// <summary>Warming up: the workout started with a warm-up, and no exercise has started yet.</summary>
    public bool WarmingUp => Session is { WarmUp: true, WarmUpEndedAt: null };

    /// <summary>Cooling down, from Start cool-down until Finish.</summary>
    public bool CoolingDown => Session.CoolDownStartedAt is not null;

    /// <summary>A cool-down is planned and hasn't started, so finishing starts it instead.</summary>
    public bool CoolDownNext => Session is { CoolDown: true, CoolDownStartedAt: null };

    /// <summary>The sets running now, which stop together: one, or a superset's round. Empty between sets.</summary>
    public List<WorkoutSet> Running => [.. Sets.Where(s => s.StoppedAt is null)];

    /// <summary>The set the break screen logs: the last one, once stopped.</summary>
    public WorkoutSet? ToLog => Sets.Count > 0 && Sets[^1].StoppedAt is not null ? Sets[^1] : null;

    /// <summary>The last set and any started with it (a superset's round), once stopped. Empty before the first.</summary>
    public List<WorkoutSet> LastRound => ToLog is { } last ? Sets[(Sets.FindLastIndex(s => s.StartedAt != last.StartedAt) + 1)..] : [];

    /// <summary>What the big button offers after the sets stopped so far.</summary>
    public PlanStep? Next { get; private set; }

    /// <summary>Where <see cref="Next"/> is in the plan: its block's index in <see cref="WorkoutPlan.Blocks"/>; null when there's none.</summary>
    public int? NextBlock { get; private set; }

    /// <summary>
    /// When the next step has no set count (nothing planned, nothing to go on from last time), I may be done with it:
    /// this is the step after moving on, or null when moving on means finishing.
    /// </summary>
    public PlanStep? MoveOn { get; private set; }

    /// <summary>True when <see cref="Next"/> goes on with what I just did and has no count, so the screen also offers to move on.</summary>
    public bool CanMoveOn { get; private set; }

    /// <summary>The exercise Change applies to when none is named: the running set's, else the next step's (a superset's first).</summary>
    public long? ChangeTarget => Running.FirstOrDefault()?.ExerciseId ?? Next?.ExerciseId;

    /// <summary>Exercises still to do in the plan, in order, supersets together; ones I went past too.</summary>
    public List<long> StillToDo() =>
        [.. WorkoutPlan.Open(Plan, [.. Sets.Where(s => s.StoppedAt is not null)], setsLastTime, Skipped).SelectMany(o => o.Members).Select(e => e.ExerciseId)];

    /// <summary>What starts this exercise now: its superset's round when it's in one still to do, else a set of it alone.</summary>
    public IReadOnlyList<long> RoundOf(long exerciseId) =>
        WorkoutPlan.Open(Plan, [.. Sets.Where(s => s.StoppedAt is not null)], setsLastTime, Skipped).Select(o => o.Step.ExerciseIds).FirstOrDefault(ids => ids.Contains(exerciseId)) ?? [exerciseId];

    /// <summary>The sets each block of the plan counted, running ones too, in the order of <see cref="WorkoutPlan.Blocks"/>.</summary>
    public List<List<WorkoutSet>> CountedSets() => WorkoutPlan.Counted(Plan, Sets, setsLastTime);

    /// <summary>The exercises' names, joined: "Pull-ups + Dips".</summary>
    public string Names(IEnumerable<long> exerciseIds) => string.Join(" + ", exerciseIds.Select(id => Exercises[id].Name));

    /// <summary>Recomputes what comes next. Called after every change.</summary>
    public void Refresh()
    {
        var exerciseIds = Plan.Select(e => e.ExerciseId).Concat(Sets.Select(s => s.ExerciseId)).Distinct();
        setsLastTime = exerciseIds.ToDictionary(id => id, id => History(id).FirstOrDefault()?.Sets.Count ?? 0);

        var stopped = Sets.Where(s => s.StoppedAt is not null).ToList();
        Next = WorkoutPlan.Next(Plan, stopped, setsLastTime, Skipped);
        NextBlock = WorkoutPlan.NextBlock(Plan, stopped, setsLastTime, Skipped);
        CanMoveOn = Next is { TargetSets: null } next && LastRound.Any(s => next.ExerciseIds.Contains(s.ExerciseId));
        if (!CanMoveOn || NextBlock is not int at) { MoveOn = null; return; }

        MoveOn = WorkoutPlan.Next(Plan, stopped, setsLastTime, [.. Skipped, .. WorkoutPlan.Blocks(Plan)[at].Select(e => e.ExerciseId)]);
    }

    /// <summary>The last 3 sessions that had this exercise, newest first.</summary>
    public IReadOnlyList<PastSession> History(long exerciseId)
    {
        if (!historyCache.TryGetValue(exerciseId, out var history)) historyCache[exerciseId] = history = ExerciseHistory.Recent(exerciseId, PastSessions, PastSets, Session.Id, 3);
        return history;
    }

    /// <summary>Where a set sits in the plan ("set 2 of 3", "round 1 of 3"), counting only the sets before its round.</summary>
    public PlanStep Describe(WorkoutSet set)
    {
        var before = Sets.Where(s => s.Order < set.Order && s.StartedAt != set.StartedAt && s.StoppedAt is not null).ToList();
        var planned = WorkoutPlan.Next(Plan, before, setsLastTime, Skipped);
        if (planned is not null && planned.ExerciseIds.Contains(set.ExerciseId)) return planned;

        var setNumber = before.Count(s => s.ExerciseId == set.ExerciseId) + 1;
        int? lastTime = setsLastTime.GetValueOrDefault(set.ExerciseId) is var count && count >= setNumber ? count : null;
        return new PlanStep([set.ExerciseId], setNumber, lastTime);
    }

    /// <summary>The pre-filled values a set started with, to tell whether its weight went up for a hit goal.</summary>
    public SetValues? SuggestionFor(WorkoutSet set)
    {
        var before = Sets.TakeWhile(s => s.Order < set.Order).Where(s => s.ExerciseId == set.ExerciseId).ToList();
        return SetSuggestion.For(before, History(set.ExerciseId).FirstOrDefault()?.Sets ?? [], HitGoalStep);
    }

    /// <summary>"Set 2 of 3", "Set 4", or "Superset · round 1 of 3".</summary>
    public static string StepLabel(PlanStep step)
    {
        var of = step.TargetSets is int target ? $" of {target}" : "";
        return step.IsRound ? $"Superset · round {step.SetNumber}{of}" : $"Set {step.SetNumber}{of}";
    }
}
