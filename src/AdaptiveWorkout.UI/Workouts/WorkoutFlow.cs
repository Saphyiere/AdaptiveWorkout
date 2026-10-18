using System.Diagnostics;
using System.Globalization;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Watch;

namespace AdaptiveWorkout.UI.Workouts;

/// <summary>
/// Starting, stopping and finishing. Every change updates the in-memory workout first, so the screen moves on at once,
/// then saves in the background; the store keeps those saves in order. The watch app opens with a new workout and closes when it ends.
/// </summary>
public sealed class WorkoutFlow(WorkoutStore store, AppSettings settings, WatchLink watch, WorkoutNotice notice)
{
    private const string RestCountdownKey = "RestCountdown";

    private Task? loading;
    private List<WorkoutSession> pastSessions = [];
    private List<WorkoutSet> pastSets = [];
    private Dictionary<string, string> preferences = [];

    /// <summary>The open workout, or null when there is none (or nothing has loaded yet).</summary>
    public WorkoutState? Current { get; private set; }

    /// <summary>Raised after every change to the open workout, e.g. one made from the watch, so a screen showing it can update.</summary>
    public event Action? Changed;

    /// <summary>What the open workout is doing now, for the watch and the notification; null when none is open.</summary>
    public WorkoutStatus? Status
    {
        get
        {
            if (Current is not { } state) return null;
            if (state.Running is { } running) return new WorkoutStatus(false, state.Exercises[running.ExerciseId].Name, running.StartedAt!.Value, null, null);

            var next = state.Next is { } step ? state.Exercises[step.ExerciseId].Name : null;
            if (state.ToLog is not { } logged) return new WorkoutStatus(true, state.Name, state.Session.StartedAt, null, next);
            if (state.Next is { AfterBreak: false }) return new WorkoutStatus(true, $"{state.Exercises[logged.ExerciseId].Name} done", logged.StoppedAt!.Value, null, next);

            DateTimeOffset? endsAt = RestCountdown ? logged.StoppedAt!.Value + RestTarget(logged.ExerciseId) : null;
            return new WorkoutStatus(true, "Rest", logged.StoppedAt!.Value, endsAt, next);
        }
    }

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
        var plan = templateId is long id ? PlanEdits.Copy(await store.GetTemplateExercisesAsync(id)) : [];
        Save(store.SaveSessionPlanAsync(session, plan));
        Current = await CreateStateAsync(session, [], plan);
        watch.Open(session);
        if (Current.Next is { } first) StartSet(Current.Exercises[first.ExerciseId]);
        else Announce();
    }

    /// <summary>Saves the set being logged, then starts a set of the exercise with suggested values.</summary>
    public void StartSet(Exercise exercise)
    {
        if (Current is not { } state) return;
        if (state.ToLog is { } logged) Save(store.UpdateSetAsync(logged));

        // Starting from a rest closes that rest's entries. The second half of a superset round doesn't, so the round is entered together.
        var midRound = state.Next is { AfterBreak: false } next && next.ExerciseId == exercise.Id;
        if (!midRound)
        {
            state.LoggedThrough = state.Sets.Count;
            state.Confirmed.Clear();
        }

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
        Announce();
    }

    /// <summary>Starts whatever comes next, e.g. from the watch when a rest is up. Does nothing while a set runs.</summary>
    public void StartNext()
    {
        if (Current is { Running: null, Next: { } next } state) StartSet(state.Exercises[next.ExerciseId]);
    }

    /// <summary>Stops the running set; for a time exercise the stopwatch becomes its duration.</summary>
    public void StopSet()
    {
        if (Current?.Running is not { } running) return;

        running.StoppedAt = DateTimeOffset.Now;
        if (Current.Exercises[running.ExerciseId].Measure == ExerciseMeasure.Time) running.DurationSeconds = (int)Math.Round((running.StoppedAt - running.StartedAt)?.TotalSeconds ?? 0);
        Current.Refresh();
        Save(store.UpdateSetAsync(running));
        Announce();
    }

    /// <summary>Saves edits to a set (weight, reps, hit goal).</summary>
    public void SaveSet(WorkoutSet set) => Save(store.UpdateSetAsync(set));

    /// <summary>Saves a set entered on the rest screen; once every set there is saved, the rest screen shows today's summary.</summary>
    public void ConfirmSet(WorkoutSet set)
    {
        if (Current is not { } state) return;

        state.Confirmed.Add(set.Order);
        Save(store.UpdateSetAsync(set));
    }

    /// <summary>Reopens this rest's entries. After a reopened workout, when nothing was entered at this rest, reopens the last set.</summary>
    public void EditRound()
    {
        if (Current is not { } state) return;

        if (state.Sets.All(s => s.Order <= state.LoggedThrough)) state.LoggedThrough = state.Sets.Count - 1;
        state.Confirmed.Clear();
    }

    public void Skip(long exerciseId)
    {
        if (Current is null) return;

        Current.Skipped.Add(exerciseId);
        Current.Refresh();
        Announce();
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
        Save(ScoreSessionsAsync());
        Current = null;
        watch.Close();
        Announce();
    }

    /// <summary>Throws away the open session and its sets, e.g. one started by mistake.</summary>
    public void Cancel()
    {
        if (Current is not { } state) return;

        Save(store.DeleteSessionAsync(state.Session));
        Current = null;
        watch.Close();
        Announce();
    }

    /// <summary>
    /// Works out how each finished workout compared with the last time of its exercises (see <see cref="Progress"/>) and saves it
    /// with the workout, once: at Finish, and for workouts finished before improvements existed.
    /// </summary>
    public async Task ScoreSessionsAsync()
    {
        await LoadAsync();
        var unscored = pastSessions.Where(s => s is { FinishedAt: not null, ExercisesCompared: null }).OrderBy(s => s.StartedAt).ToList();
        if (unscored.Count == 0) return;

        var exercises = (await store.GetExercisesAsync()).ToDictionary(e => e.Id);
        var bodyWeights = await store.GetBodyWeightsAsync();
        foreach (var session in unscored)
        {
            (session.Improvement, session.ExercisesCompared) = Progress.Improvement(session, pastSessions, pastSets, exercises, bodyWeights);
            Save(store.SaveImprovementAsync(session));
        }
    }

    /// <summary>Deletes a finished session and its sets, so history and suggestions forget it.</summary>
    public async Task DeleteSessionAsync(WorkoutSession session)
    {
        await LoadAsync();
        pastSessions.RemoveAll(s => s.Id == session.Id);
        pastSets.RemoveAll(s => s.SessionId == session.Id);
        Save(store.DeleteSessionAsync(session));
    }

    /// <summary>Does another exercise first; the one Change was opened on waits until it's done. Today's plan only.</summary>
    public void DoFirst(Exercise exercise)
    {
        if (Current is not { ChangeTarget: long target } state) return;

        ChangePlan(state, PlanEdits.DoFirst(state.Plan, exercise.Id, target), exercise);
    }

    /// <summary>Puts another exercise in place of the one Change was opened on, today only. One already further on in the plan moves up instead.</summary>
    public void SwapFor(Exercise exercise)
    {
        if (Current is not { ChangeTarget: long target } state) return;

        state.Exercises.TryAdd(exercise.Id, exercise);
        var inPlan = state.Plan.Any(e => e.ExerciseId == exercise.Id);
        ChangePlan(state, inPlan ? PlanEdits.Drop(PlanEdits.DoFirst(state.Plan, exercise.Id, target), target) : PlanEdits.Swap(state.Plan, target, exercise.Id), exercise);
    }

    /// <summary>Leaves the exercise Change was opened on out of today's workout.</summary>
    public void Drop()
    {
        if (Current is not { ChangeTarget: long target } state) return;

        ChangePlan(state, PlanEdits.Drop(state.Plan, target), null);
    }

    /// <summary>Adds an exercise after everything else in today's plan.</summary>
    public void AddExercise(Exercise exercise)
    {
        if (Current is not { } state) return;

        state.Exercises.TryAdd(exercise.Id, exercise);
        ChangePlan(state, PlanEdits.Append(state.Plan, [new TemplateExercise { ExerciseId = exercise.Id }]), null);
    }

    /// <summary>Adds a whole session's exercises after everything else in today's plan; the workout keeps its name.</summary>
    public async Task AddSessionAsync(long templateId)
    {
        if (Current is not { } state) return;

        var entries = await store.GetTemplateExercisesAsync(templateId);
        foreach (var exercise in await store.GetExercisesAsync()) state.Exercises.TryAdd(exercise.Id, exercise);
        ChangePlan(state, PlanEdits.Append(state.Plan, entries), null);
    }

    /// <summary>Whether a rest counts down to the next set, and starts it at zero, instead of counting up.</summary>
    public bool RestCountdown => preferences.GetValueOrDefault(RestCountdownKey) == "on";

    /// <summary>True until the rest countdown has been tried, so the break screen can say how.</summary>
    public bool RestCountdownUntried => !preferences.ContainsKey(RestCountdownKey);

    public void SetRestCountdown(bool on) => SetPreference(RestCountdownKey, on ? "on" : "off");

    /// <summary>The countdown for a rest after this exercise: the one I set last time, else my typical rest after it last time, else 1:30.</summary>
    public TimeSpan RestTarget(long exerciseId)
    {
        if (preferences.GetValueOrDefault(RestTargetKey(exerciseId)) is string saved) return TimeSpan.FromSeconds(int.Parse(saved, CultureInfo.InvariantCulture));

        var last = Current?.History(exerciseId).FirstOrDefault();
        return (last is null ? null : Rest.Typical([.. pastSets.Where(s => s.SessionId == last.Session.Id)], exerciseId)) ?? Rest.Default;
    }

    /// <summary>Moves this exercise's rest countdown by some seconds; it's remembered for next time.</summary>
    public void AdjustRestTarget(long exerciseId, int seconds) =>
        SetPreference(RestTargetKey(exerciseId), Math.Clamp((int)RestTarget(exerciseId).TotalSeconds + seconds, 15, 600).ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates (or links to) an exercise picked by name mid-workout.</summary>
    public Task<Exercise> GetOrCreateExerciseAsync(string name, ExerciseMeasure measure) => store.GetOrCreateExerciseAsync(name, measure);

    private async Task LoadHistoryAsync()
    {
        preferences = await store.GetSettingsAsync();
        var sessions = await store.GetSessionsAsync();
        var sets = await store.GetSetsAsync();
        var open = sessions.LastOrDefault(s => s.FinishedAt is null);
        pastSessions = [.. sessions.Where(s => s != open)];
        pastSets = [.. sets.Where(s => s.SessionId != open?.Id)];
        if (open is null) return;

        // A workout started before workouts kept their own plan follows its template.
        var plan = await store.GetSessionPlanAsync(open.Id);
        if (plan.Count == 0 && open.TemplateId is long templateId) plan = PlanEdits.Copy(await store.GetTemplateExercisesAsync(templateId));
        Current = await CreateStateAsync(open, [.. sets.Where(s => s.SessionId == open.Id)], plan);
        Announce();
    }

    // Replaces today's plan and saves it. A running set belonged to the exercise that changed, so it's thrown away and
    // `startNow` (or, after Drop, whatever comes next) starts in its place.
    private void ChangePlan(WorkoutState state, List<TemplateExercise> plan, Exercise? startNow)
    {
        var running = state.Running;
        if (running is not null)
        {
            state.Sets.Remove(running);
            Save(store.DeleteSetAsync(running));
        }

        state.Plan = plan;
        state.Refresh();
        Save(store.SaveSessionPlanAsync(state.Session, plan));

        var next = running is null ? null : startNow ?? (state.Next is { } step ? state.Exercises[step.ExerciseId] : null);
        if (next is not null) StartSet(next);
        else Announce();
    }

    private async Task<WorkoutState> CreateStateAsync(WorkoutSession session, List<WorkoutSet> sets, List<TemplateExercise> plan)
    {
        var template = session.TemplateId is long templateId ? await store.GetTemplateAsync(templateId) : null;
        var state = new WorkoutState
        {
            Session = session,
            Template = template,
            Plan = plan,
            Exercises = (await store.GetExercisesAsync()).ToDictionary(e => e.Id),
            Sets = sets,
            PastSessions = pastSessions,
            PastSets = pastSets,
            HitGoalStep = settings.HitGoalStep,
            LoggedThrough = sets.Count,
        };
        state.Refresh();
        return state;
    }

    private static string RestTargetKey(long exerciseId) => $"RestTarget:{exerciseId}";

    private void SetPreference(string key, string value)
    {
        preferences[key] = value;
        Save(store.SetSettingAsync(key, value));
        Announce();
    }

    // Tells the watch, the notification and any screen showing the workout what it's doing now.
    private void Announce()
    {
        var status = Status;
        if (status is not null) watch.Show(status);
        notice.Show(status);
        Changed?.Invoke();
    }

    // Saves run in the background; a failure is logged rather than holding up the screen.
    private static void Save(Task write) => write.ContinueWith(t => Debug.WriteLine($"AdaptiveWorkout: saving failed: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
}
