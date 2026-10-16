using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;

namespace AdaptiveWorkout.UI.Workouts;

/// <summary>Starting, stopping and finishing: every change to the open workout goes through here.</summary>
public sealed class WorkoutFlow(WorkoutStore store, AppSettings settings)
{
    // Skipping lives only for the open workout; it isn't worth storing.
    private readonly Dictionary<long, HashSet<long>> skippedBySession = [];

    /// <summary>The open workout, or null when there is none.</summary>
    public async Task<WorkoutState?> LoadAsync()
    {
        var session = await store.GetOpenSessionAsync();
        if (session is null) return null;

        var template = session.TemplateId is long templateId ? await store.GetTemplateAsync(templateId) : null;
        IReadOnlyList<TemplateExercise> plan = template is null ? [] : await store.GetTemplateExercisesAsync(template.Id);
        var exercises = await store.GetExercisesAsync();
        var allSets = await store.GetSetsAsync();
        return new WorkoutState
        {
            Session = session,
            Template = template,
            Plan = plan,
            Exercises = exercises.ToDictionary(e => e.Id),
            Sets = [.. allSets.Where(s => s.SessionId == session.Id)],
            AllSessions = await store.GetSessionsAsync(),
            AllSets = allSets,
            Skipped = skippedBySession.GetValueOrDefault(session.Id) ?? [],
            HitGoalStep = settings.HitGoalStep,
        };
    }

    /// <summary>Starts a session now. With a planned first exercise, its first set starts right away.</summary>
    public async Task StartSessionAsync(long? templateId)
    {
        await store.StartSessionAsync(templateId, DateTimeOffset.Now);
        var state = await LoadAsync();
        if (state?.Next is { } first) await StartSetAsync(state, state.Exercises[first.ExerciseId]);
    }

    /// <summary>Saves the set being logged, then starts a set of the exercise with suggested values.</summary>
    public async Task StartSetAsync(WorkoutState state, Exercise exercise)
    {
        if (state.ToLog is { } logged) await store.UpdateSetAsync(logged);

        var thisSession = state.Sets.Where(s => s.ExerciseId == exercise.Id).ToList();
        var lastTime = state.History(exercise.Id, 1).FirstOrDefault()?.Sets ?? [];
        var values = exercise.Measure == ExerciseMeasure.Weight ? SetSuggestion.For(thisSession, lastTime, settings.HitGoalStep) : null;
        await store.InsertSetAsync(new WorkoutSet
        {
            SessionId = state.Session.Id,
            ExerciseId = exercise.Id,
            Order = state.Sets.Count + 1,
            StartedAt = DateTimeOffset.Now,
            Weight = values?.Weight,
            WeightUnit = values?.Unit ?? settings.Unit,
            Reps = values?.Reps,
        });
    }

    /// <summary>Stops the running set; for a time exercise the stopwatch becomes its duration.</summary>
    public async Task StopSetAsync(WorkoutState state)
    {
        if (state.Running is not { } running) return;

        running.StoppedAt = DateTimeOffset.Now;
        if (state.Exercises[running.ExerciseId].Measure == ExerciseMeasure.Time) running.DurationSeconds = (int)Math.Round((running.StoppedAt - running.StartedAt)?.TotalSeconds ?? 0);
        await store.UpdateSetAsync(running);
    }

    public Task SaveSetAsync(WorkoutSet set) => store.UpdateSetAsync(set);

    public void Skip(WorkoutState state, long exerciseId)
    {
        if (!skippedBySession.TryGetValue(state.Session.Id, out var skipped)) skippedBySession[state.Session.Id] = skipped = [];
        skipped.Add(exerciseId);
    }

    /// <summary>Stops a running set, saves the one being logged, and closes the session.</summary>
    public async Task FinishAsync(WorkoutState state)
    {
        await StopSetAsync(state);
        if (state.ToLog is { } logged) await store.UpdateSetAsync(logged);
        await store.FinishSessionAsync(state.Session.Id, DateTimeOffset.Now);
        skippedBySession.Remove(state.Session.Id);
    }

    /// <summary>Throws away a session that has no sets yet.</summary>
    public async Task CancelAsync(WorkoutState state)
    {
        if (state.Sets.Count > 0) return;

        await store.DeleteSessionAsync(state.Session.Id);
        skippedBySession.Remove(state.Session.Id);
    }
}
