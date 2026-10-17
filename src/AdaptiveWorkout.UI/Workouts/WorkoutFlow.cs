using System.Diagnostics;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;

namespace AdaptiveWorkout.UI.Workouts;

/// <summary>
/// Starting, stopping and finishing. Every change updates the in-memory workout first, so the screen moves on at once,
/// then saves in the background; the store keeps those saves in order. The watch app opens with a new workout and closes when it ends.
/// </summary>
public sealed class WorkoutFlow(WorkoutStore store, AppSettings settings, WatchLink watch)
{
    private Task? loading;
    private List<WorkoutSession> pastSessions = [];
    private List<WorkoutSet> pastSets = [];

    /// <summary>The open workout, or null when there is none (or nothing has loaded yet).</summary>
    public WorkoutState? Current { get; private set; }

    /// <summary>Loads history and any open workout once; later calls return what's in memory.</summary>
    public async Task<WorkoutState?> LoadAsync()
    {
        await (loading ??= LoadHistoryAsync());
        return Current;
    }

    /// <summary>Starts a session now, or resumes the open one. With a planned first exercise, its first set starts right away.</summary>
    public async Task StartSessionAsync(long? templateId)
    {
        if (await LoadAsync() is not null) return;

        var session = new WorkoutSession { TemplateId = templateId, StartedAt = DateTimeOffset.Now };
        Save(store.StartSessionAsync(session));
        Current = await CreateStateAsync(session, []);
        watch.Open(session);
        if (Current.Next is { } first) StartSet(Current.Exercises[first.ExerciseId]);
    }

    /// <summary>Saves the set being logged, then starts a set of the exercise with suggested values.</summary>
    public void StartSet(Exercise exercise)
    {
        if (Current is not { } state) return;
        if (state.ToLog is { } logged) Save(store.UpdateSetAsync(logged));

        var thisSession = state.Sets.Where(s => s.ExerciseId == exercise.Id).ToList();
        var lastTime = state.History(exercise.Id).FirstOrDefault()?.Sets ?? [];
        var values = exercise.Measure == ExerciseMeasure.Weight ? SetSuggestion.For(thisSession, lastTime, settings.HitGoalStep) : null;
        var set = new WorkoutSet
        {
            ExerciseId = exercise.Id,
            Order = state.Sets.Count + 1,
            StartedAt = DateTimeOffset.Now,
            Weight = values?.Weight,
            WeightUnit = values?.Unit ?? settings.Unit,
            Reps = values?.Reps,
        };
        state.Exercises.TryAdd(exercise.Id, exercise);
        state.Sets.Add(set);
        state.Refresh();
        Save(store.InsertSetAsync(state.Session, set));
    }

    /// <summary>Stops the running set; for a time exercise the stopwatch becomes its duration.</summary>
    public void StopSet()
    {
        if (Current?.Running is not { } running) return;

        running.StoppedAt = DateTimeOffset.Now;
        if (Current.Exercises[running.ExerciseId].Measure == ExerciseMeasure.Time) running.DurationSeconds = (int)Math.Round((running.StoppedAt - running.StartedAt)?.TotalSeconds ?? 0);
        Current.Refresh();
        Save(store.UpdateSetAsync(running));
    }

    /// <summary>Saves edits to a set (weight, reps, hit goal).</summary>
    public void SaveSet(WorkoutSet set) => Save(store.UpdateSetAsync(set));

    public void Skip(long exerciseId)
    {
        if (Current is null) return;

        Current.Skipped.Add(exerciseId);
        Current.Refresh();
    }

    /// <summary>Stops a running set, saves the one being logged, and closes the session.</summary>
    public void Finish()
    {
        if (Current is not { } state) return;

        StopSet();
        if (state.ToLog is { } logged) Save(store.UpdateSetAsync(logged));
        state.Session.FinishedAt = DateTimeOffset.Now;
        Save(store.FinishSessionAsync(state.Session));
        pastSessions.Add(state.Session);
        pastSets.AddRange(state.Sets);
        Current = null;
        watch.Close();
    }

    /// <summary>Throws away a session that has no sets yet.</summary>
    public void Cancel()
    {
        if (Current is not { Sets.Count: 0 } state) return;

        Save(store.DeleteSessionAsync(state.Session));
        Current = null;
        watch.Close();
    }

    /// <summary>Creates (or links to) an exercise picked by name mid-workout.</summary>
    public Task<Exercise> GetOrCreateExerciseAsync(string name, ExerciseMeasure measure) => store.GetOrCreateExerciseAsync(name, measure);

    private async Task LoadHistoryAsync()
    {
        var sessions = await store.GetSessionsAsync();
        var sets = await store.GetSetsAsync();
        var open = sessions.LastOrDefault(s => s.FinishedAt is null);
        pastSessions = [.. sessions.Where(s => s != open)];
        pastSets = [.. sets.Where(s => s.SessionId != open?.Id)];
        if (open is not null) Current = await CreateStateAsync(open, [.. sets.Where(s => s.SessionId == open.Id)]);
    }

    private async Task<WorkoutState> CreateStateAsync(WorkoutSession session, List<WorkoutSet> sets)
    {
        var template = session.TemplateId is long templateId ? await store.GetTemplateAsync(templateId) : null;
        var state = new WorkoutState
        {
            Session = session,
            Template = template,
            Plan = template is null ? [] : await store.GetTemplateExercisesAsync(template.Id),
            Exercises = (await store.GetExercisesAsync()).ToDictionary(e => e.Id),
            Sets = sets,
            PastSessions = pastSessions,
            PastSets = pastSets,
            HitGoalStep = settings.HitGoalStep,
        };
        state.Refresh();
        return state;
    }

    // Saves run in the background; a failure is logged rather than holding up the screen.
    private static void Save(Task write) => write.ContinueWith(t => Debug.WriteLine($"AdaptiveWorkout: saving failed: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
}
