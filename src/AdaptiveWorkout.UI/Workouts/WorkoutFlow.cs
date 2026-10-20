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
            if (state.Session.CoolDownStartedAt is { } coolingSince) return new WorkoutStatus(true, "Cool-down", coolingSince, null, null);
            if (state.Running is [var first, ..] running) return new WorkoutStatus(false, state.Names(running.Select(s => s.ExerciseId)), first.StartedAt!.Value, null, null);

            // The warm-up waits for the first exercise like a rest, counting up, so the watch can start it too. After the last
            // exercise, a planned cool-down is what's next.
            var next = state.Next is { } step ? state.Names(step.ExerciseIds) : state is { WarmingUp: false, CoolDownNext: true } ? "cool-down" : null;
            if (state.WarmingUp) return new WorkoutStatus(true, "Warm-up", state.Session.StartedAt, null, next);
            if (state.ToLog is not { } logged) return new WorkoutStatus(true, state.Name, state.Session.StartedAt, null, next);

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

    /// <summary>
    /// Starts a session now, or resumes the open one. A session with a warm-up starts with it, until the big button starts the first
    /// exercise; otherwise a planned first exercise's first set starts right away.
    /// </summary>
    public async Task StartSessionAsync(long? templateId)
    {
        if (await LoadAsync() is not null) return;

        var template = templateId is long id ? await store.GetTemplateAsync(id) : null;
        var session = new WorkoutSession
        {
            TemplateId = templateId,
            StartedAt = DateTimeOffset.Now,
            WarmUp = template?.WarmUp == true,
            CoolDown = template?.CoolDown == true,
        };
        Save(store.StartSessionAsync(session));
        var plan = template is not null ? PlanEdits.Copy(await store.GetTemplateExercisesAsync(template.Id)) : [];
        Save(store.SaveSessionPlanAsync(session, plan));
        Current = await CreateStateAsync(session, [], plan);
        watch.Open(session);
        if (!session.WarmUp && Current.Next is { } first) StartSets(first.ExerciseIds);
        else Announce();
    }

    /// <summary>
    /// Starts a set of each exercise, with suggested values: one, or a superset's round, whose sets start and stop together and
    /// so share their time and the heart rate during it. Saves the entries of the rest it ends.
    /// </summary>
    public void StartSets(IReadOnlyList<long> exerciseIds)
    {
        if (Current is not { } state || exerciseIds.Count == 0) return;

        // The first exercise ends the warm-up as it starts. A set started during the cool-down puts the cool-down off until the end again.
        var now = DateTimeOffset.Now;
        if (state.WarmingUp || state.CoolingDown)
        {
            if (state.WarmingUp) state.Session.WarmUpEndedAt = now;
            state.Session.CoolDownStartedAt = null;
            Save(store.SaveWarmUpAndCoolDownAsync(state.Session));
        }

        // Starting closes the rest's entries; anything not saved yet keeps what's in its fields.
        foreach (var entry in state.ToConfirm) Save(store.UpdateSetAsync(entry));
        state.LoggedThrough = state.LastOrder;
        state.Confirmed.Clear();

        foreach (var exerciseId in exerciseIds)
        {
            var exercise = state.Exercises[exerciseId];
            var thisSession = state.Sets.Where(s => s.ExerciseId == exerciseId).ToList();
            var lastTime = state.History(exerciseId).FirstOrDefault()?.Sets ?? [];
            var values = exercise.Measure == ExerciseMeasure.Weight ? SetSuggestion.For(thisSession, lastTime, settings.HitGoalStep) : null;
            var set = new WorkoutSet
            {
                ExerciseId = exerciseId,
                Order = state.LastOrder + 1,
                StartedAt = now,
                Weight = values?.Weight,
                WeightUnit = values?.Unit ?? settings.Unit,
                Reps = values?.Reps,
            };
            state.Sets.Add(set);
            Save(store.InsertSetAsync(state.Session, set));
        }

        state.Refresh();
        Announce();
    }

    /// <summary>Starts whatever comes next, e.g. from the watch: the next set or round, else a planned cool-down. Does nothing while a set runs.</summary>
    public void StartNext()
    {
        if (Current is { Running: [], Next: { } next }) StartSets(next.ExerciseIds);
        else StartCoolDown();
    }

    /// <summary>Another set of what I just did (another round of a superset), e.g. past its planned sets.</summary>
    public void StartAnother()
    {
        if (Current is { Running: [], LastRound: [_, ..] round }) StartSets([.. round.Select(s => s.ExerciseId)]);
    }

    /// <summary>
    /// Starts an exercise picked from the list in place of what's next; one of a superset starts its round. One the plan doesn't
    /// have joins it right after what's next, so the plan carries on after it and what it took the place of isn't offered again.
    /// </summary>
    public void StartPicked(Exercise exercise)
    {
        if (Current is not { } state) return;

        state.Exercises.TryAdd(exercise.Id, exercise);
        if (state.Plan.Count > 0 && state.Plan.All(e => e.ExerciseId != exercise.Id))
        {
            state.Plan = PlanEdits.InsertAfter(state.Plan, exercise.Id, state.NextBlock);
            state.Refresh();
            Save(store.SaveSessionPlanAsync(state.Session, state.Plan));
        }

        StartSets(state.RoundOf(exercise.Id));
    }

    /// <summary>Stops the running set, or a superset's round, all at once; for a time exercise the stopwatch becomes its duration.</summary>
    public void StopSet()
    {
        if (Current is not { Running: [_, ..] running } state) return;

        var now = DateTimeOffset.Now;
        foreach (var set in running)
        {
            set.StoppedAt = now;
            if (state.Exercises[set.ExerciseId].Measure == ExerciseMeasure.Time) set.DurationSeconds = (int)Math.Round((now - set.StartedAt!.Value).TotalSeconds);
            Save(store.UpdateSetAsync(set));
        }

        state.Refresh();
        Announce();
    }

    /// <summary>Saves edits to a set (weight, reps, hit goal).</summary>
    public void SaveSet(WorkoutSet set) => Save(store.UpdateSetAsync(set));

    /// <summary>Deletes a finished set of the open workout, e.g. one done by mistake. What's next follows from the sets left.</summary>
    public void DeleteSet(WorkoutSet set)
    {
        if (Current is not { } state || set.StoppedAt is null || !state.Sets.Remove(set)) return;

        state.Confirmed.Remove(set.Order);
        state.Refresh();
        Save(store.DeleteSetAsync(set));
        Announce();
    }

    /// <summary>Saves a change to a set of a finished workout, then works out again how it and every later one compared.</summary>
    public async Task SavePastSetAsync(WorkoutSet set)
    {
        await LoadAsync();
        var at = pastSets.FindIndex(s => s.Id == set.Id);
        if (at >= 0) pastSets[at] = set;
        Save(store.UpdateSetAsync(set));
        await RescoreFromAsync(set.SessionId);
    }

    /// <summary>Deletes a set of a finished workout, then works out again how it and every later one compared.</summary>
    public async Task DeletePastSetAsync(WorkoutSet set)
    {
        await LoadAsync();
        pastSets.RemoveAll(s => s.Id == set.Id);
        Save(store.DeleteSetAsync(set));
        await RescoreFromAsync(set.SessionId);
    }

    /// <summary>Saves a set entered on the rest screen; once every set there is saved, the rest screen shows today's summary.</summary>
    public void ConfirmSet(WorkoutSet set)
    {
        if (Current is not { } state) return;

        state.Confirmed.Add(set.Order);
        Save(store.UpdateSetAsync(set));
    }

    /// <summary>Passes over an exercise (or a superset's) in today's plan.</summary>
    public void Skip(IEnumerable<long> exerciseIds)
    {
        if (Current is null) return;

        Current.Skipped.UnionWith(exerciseIds);
        Current.Refresh();
        Announce();
    }

    /// <summary>After the last exercise: saves the set being logged and starts the cool-down, which runs until Finish.</summary>
    public void StartCoolDown()
    {
        if (Current is not { Running: [], WarmingUp: false, CoolDownNext: true } state) return;

        foreach (var entry in state.ToConfirm) Save(store.UpdateSetAsync(entry));
        state.LoggedThrough = state.LastOrder;
        state.Confirmed.Clear();
        state.Session.CoolDownStartedAt = DateTimeOffset.Now;
        Save(store.SaveWarmUpAndCoolDownAsync(state.Session));
        Announce();
    }

    /// <summary>Stops a running set, saves the one being logged, and closes the session; a cool-down ends with it.</summary>
    public void Finish()
    {
        if (Current is not { } state) return;

        StopSet();
        foreach (var entry in state.ToConfirm) Save(store.UpdateSetAsync(entry));
        state.Session.FinishedAt = DateTimeOffset.Now;

        // Finished during the warm-up: it was all warm-up.
        if (state.WarmingUp)
        {
            state.Session.WarmUpEndedAt = state.Session.FinishedAt;
            Save(store.SaveWarmUpAndCoolDownAsync(state.Session));
        }

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

    /// <summary>
    /// Does another exercise first; the one Change was opened on (the running or next step's) waits until it's done. Part way
    /// through it, the sets still to do come right after the other one. Today's plan only.
    /// </summary>
    public void DoFirst(Exercise exercise, long target)
    {
        if (Current is not { } state) return;

        var step = state.Running is [var first, ..] ? state.Describe(first) : state.Next;
        var plan = step is { SetNumber: > 1 } && state.NextBlock is int block
            ? PlanEdits.DoFirst(state.Plan, exercise.Id, block, step.SetNumber - 1, step.TargetSets - (step.SetNumber - 1))
            : PlanEdits.DoFirst(state.Plan, exercise.Id, target);
        ChangePlan(state, plan, exercise);
    }

    /// <summary>
    /// Puts another exercise in the place of the one Change was opened on, today only, with its sets. One further on in the plan,
    /// not started yet, moves up instead; one already done today goes in afresh.
    /// </summary>
    public void SwapFor(Exercise exercise, long target)
    {
        if (Current is not { } state) return;

        state.Exercises.TryAdd(exercise.Id, exercise);
        var moveUp = state.Plan.Any(e => e.ExerciseId == exercise.Id) && state.Sets.All(s => s.ExerciseId != exercise.Id);
        ChangePlan(state, moveUp ? PlanEdits.Drop(PlanEdits.DoFirst(state.Plan, exercise.Id, target), target) : PlanEdits.Swap(state.Plan, target, exercise.Id), exercise);
    }

    /// <summary>Leaves the exercise Change was opened on out of today's workout.</summary>
    public void Drop(long target)
    {
        if (Current is not { } state) return;

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

    // Replaces today's plan and saves it. A running set (or round) belonged to what changed, so it's thrown away and `startNow`,
    // with its superset, starts in its place; after Drop, whatever comes next.
    private void ChangePlan(WorkoutState state, List<TemplateExercise> plan, Exercise? startNow)
    {
        var running = state.Running;
        foreach (var set in running)
        {
            state.Sets.Remove(set);
            Save(store.DeleteSetAsync(set));
        }

        state.Plan = plan;
        state.Refresh();
        Save(store.SaveSessionPlanAsync(state.Session, plan));

        var next = running.Count == 0 ? null : startNow is not null ? state.RoundOf(startNow.Id) : state.Next?.ExerciseIds;
        if (next is not null) StartSets(next);
        else Announce();
    }

    // A finished workout's sets changed: it, and every one after it, compared against them, so each is worked out again.
    private async Task RescoreFromAsync(long sessionId)
    {
        if (pastSessions.Find(s => s.Id == sessionId) is not { } changed) return;

        foreach (var session in pastSessions.Where(s => s.StartedAt >= changed.StartedAt)) (session.Improvement, session.ExercisesCompared) = (null, null);
        await ScoreSessionsAsync();
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
            LoggedThrough = sets.Count == 0 ? 0 : sets[^1].Order,
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
