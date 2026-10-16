using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.UI.Workouts;

/// <summary>A snapshot of the open workout and what the screens derive from it.</summary>
public sealed class WorkoutState
{
    public required WorkoutSession Session { get; init; }
    public SessionTemplate? Template { get; init; }

    /// <summary>The template's exercises; empty when just tracking.</summary>
    public required IReadOnlyList<TemplateExercise> Plan { get; init; }

    public required IReadOnlyDictionary<long, Exercise> Exercises { get; init; }

    /// <summary>This session's sets, in order. The last one may still be running.</summary>
    public required IReadOnlyList<WorkoutSet> Sets { get; init; }

    public required IReadOnlyList<WorkoutSession> AllSessions { get; init; }
    public required IReadOnlyList<WorkoutSet> AllSets { get; init; }
    public required IReadOnlyCollection<long> Skipped { get; init; }
    public required double HitGoalStep { get; init; }

    public string Name => Template?.Name ?? "Workout";

    public WorkoutSet? Running => Sets.LastOrDefault(s => s.StoppedAt is null);

    /// <summary>The set the break screen logs: the last one, once stopped.</summary>
    public WorkoutSet? ToLog => Running is null ? Sets.LastOrDefault() : null;

    /// <summary>What the big button offers after the sets stopped so far.</summary>
    public PlanStep? Next => WorkoutPlan.Next(Plan, [.. Sets.Where(s => s.StoppedAt is not null)], SetsLastTime(), Skipped);

    public IReadOnlyList<PastSession> History(long exerciseId, int count) => ExerciseHistory.Recent(exerciseId, AllSessions, AllSets, Session.Id, count);

    /// <summary>Where a set sits in the plan ("set 2 of 3", "round 1 of 3"), counting only the sets before it.</summary>
    public PlanStep Describe(WorkoutSet set)
    {
        var before = Sets.TakeWhile(s => s.Id != set.Id).Where(s => s.StoppedAt is not null).ToList();
        var planned = WorkoutPlan.Next(Plan, before, SetsLastTime(), Skipped);
        if (planned?.ExerciseId == set.ExerciseId) return planned;

        int? lastTime = SetsLastTime().GetValueOrDefault(set.ExerciseId) is > 0 and var count ? count : null;
        return new PlanStep(set.ExerciseId, before.Count(s => s.ExerciseId == set.ExerciseId) + 1, lastTime, null, true);
    }

    /// <summary>The pre-filled values a set started with, to tell whether its weight went up for a hit goal.</summary>
    public SetValues? SuggestionFor(WorkoutSet set)
    {
        var before = Sets.TakeWhile(s => s.Id != set.Id).Where(s => s.ExerciseId == set.ExerciseId).ToList();
        var lastTime = History(set.ExerciseId, 1).FirstOrDefault()?.Sets ?? [];
        return SetSuggestion.For(before, lastTime, HitGoalStep);
    }

    /// <summary>"Set 2 of 3", "Set 4", or "Superset · round 1 of 3".</summary>
    public static string StepLabel(PlanStep step)
    {
        var of = step.TargetSets is int target ? $" of {target}" : "";
        return step.SupersetRound is int round ? $"Superset · round {round}{of}" : $"Set {step.SetNumber}{of}";
    }

    private Dictionary<long, int> SetsLastTime()
    {
        var exerciseIds = Plan.Select(e => e.ExerciseId).Concat(Sets.Select(s => s.ExerciseId)).Distinct();
        return exerciseIds.ToDictionary(id => id, id => History(id, 1).FirstOrDefault()?.Sets.Count ?? 0);
    }
}
