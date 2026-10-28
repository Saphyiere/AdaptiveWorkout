using System.Data;
using System.Globalization;
using AdaptiveWorkout.Core;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AdaptiveWorkout.UI.Storage;

/// <summary>
/// Everything the app records, in one SQLite file on the phone.
/// All work runs on a background thread, one piece at a time in call order: the screen never waits on SQLite,
/// a write can be started without waiting for it, and every read sees the writes started before it.
/// </summary>
public sealed class WorkoutStore(string databasePath)
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Routine (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS SessionTemplate (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, "Order" INTEGER NOT NULL, WarmUp INTEGER NOT NULL DEFAULT 0, CoolDown INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS RoutineDay (RoutineId INTEGER NOT NULL REFERENCES Routine (Id) ON DELETE CASCADE, Day INTEGER NOT NULL, TemplateId INTEGER REFERENCES SessionTemplate (Id), PRIMARY KEY (RoutineId, Day));
        CREATE TABLE IF NOT EXISTS Setting (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS Exercise (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, NameKey TEXT NOT NULL UNIQUE, Measure INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS TemplateExercise (Id INTEGER PRIMARY KEY AUTOINCREMENT, TemplateId INTEGER NOT NULL REFERENCES SessionTemplate (Id) ON DELETE CASCADE, ExerciseId INTEGER NOT NULL REFERENCES Exercise (Id), "Order" INTEGER NOT NULL, PlannedSets INTEGER, SupersetGroup INTEGER);
        CREATE TABLE IF NOT EXISTS WorkoutSession (Id INTEGER PRIMARY KEY AUTOINCREMENT, TemplateId INTEGER REFERENCES SessionTemplate (Id), StartedAt TEXT NOT NULL, FinishedAt TEXT, LoggedAfterward INTEGER NOT NULL DEFAULT 0, Improvement REAL, ExercisesCompared INTEGER, WarmUp INTEGER NOT NULL DEFAULT 0, WarmUpEndedAt TEXT, CoolDown INTEGER NOT NULL DEFAULT 0, CoolDownStartedAt TEXT, AverageBpm REAL);
        CREATE TABLE IF NOT EXISTS WorkoutSet (Id INTEGER PRIMARY KEY AUTOINCREMENT, SessionId INTEGER NOT NULL REFERENCES WorkoutSession (Id) ON DELETE CASCADE, ExerciseId INTEGER NOT NULL REFERENCES Exercise (Id), "Order" INTEGER NOT NULL, StartedAt TEXT, StoppedAt TEXT, Weight REAL, WeightUnit INTEGER NOT NULL, Reps INTEGER, DurationSeconds INTEGER, HitGoal INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS FoodEntry (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Calories INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS BodyWeightEntry (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Weight REAL NOT NULL, WeightUnit INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS HeartRateSample (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Bpm INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS SessionExercise (Id INTEGER PRIMARY KEY AUTOINCREMENT, SessionId INTEGER NOT NULL REFERENCES WorkoutSession (Id) ON DELETE CASCADE, ExerciseId INTEGER NOT NULL REFERENCES Exercise (Id), "Order" INTEGER NOT NULL, PlannedSets INTEGER, SupersetGroup INTEGER);
        -- One workout's sets, and the heart rate during it, found without reading every row ever logged.
        CREATE INDEX IF NOT EXISTS WorkoutSetBySession ON WorkoutSet (SessionId, "Order");
        CREATE INDEX IF NOT EXISTS HeartRateSampleByTime ON HeartRateSample (At);
        """;

    // Sessions used to belong to one routine; now a routine's days point at them. SQLite can't drop a column a foreign key
    // uses, so the table is rebuilt with foreign keys off for the copy (ids stay, so everything pointing at sessions still does).
    private const string SessionsWithoutRoutine = """
        PRAGMA foreign_keys = OFF;
        BEGIN;
        CREATE TABLE SessionTemplate_new (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, "Order" INTEGER NOT NULL);
        INSERT INTO SessionTemplate_new (Id, Name, "Order") SELECT Id, Name, "Order" FROM SessionTemplate;
        DROP TABLE SessionTemplate;
        ALTER TABLE SessionTemplate_new RENAME TO SessionTemplate;
        COMMIT;
        PRAGMA foreign_keys = ON;
        """;

    // Workouts got their improvement (worked out once, after Finish) after the table existed.
    private const string SessionImprovement = "ALTER TABLE WorkoutSession ADD COLUMN Improvement REAL; ALTER TABLE WorkoutSession ADD COLUMN ExercisesCompared INTEGER;";

    // Sessions got an optional warm-up and cool-down, and workouts the times of theirs, after the tables existed. All or nothing.
    private const string WarmUpAndCoolDown = """
        BEGIN;
        ALTER TABLE SessionTemplate ADD COLUMN WarmUp INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE SessionTemplate ADD COLUMN CoolDown INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE WorkoutSession ADD COLUMN WarmUp INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE WorkoutSession ADD COLUMN WarmUpEndedAt TEXT;
        ALTER TABLE WorkoutSession ADD COLUMN CoolDown INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE WorkoutSession ADD COLUMN CoolDownStartedAt TEXT;
        COMMIT;
        """;

    // Workouts got their average heart rate (worked out once, at Finish) after the table existed.
    private const string SessionHeartRate = "ALTER TABLE WorkoutSession ADD COLUMN AverageBpm REAL;";

    private const string ActiveRoutineKey = "ActiveRoutineId";
    private const string CalorieGoalKey = "CalorieGoal", SexKey = "Sex", BirthYearKey = "BirthYear", HeightKey = "HeightCm";

    // How I spend a usual day outside workouts. (An "Activity" before it counted workouts in, so it's left behind.)
    private const string DailyActivityKey = "DailyActivity";

    private readonly string connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString();
    private readonly Lock laneGate = new();
    private Task lane = Task.CompletedTask;
    private bool schemaReady;

    static WorkoutStore()
    {
        // SQLite keeps dates as text; Dapper has to be told how to read them back.
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

    public Task<List<Routine>> GetRoutinesAsync() => RunAsync(async c => (await c.QueryAsync<Routine>("SELECT Id, Name FROM Routine ORDER BY Name")).AsList());

    /// <summary>The routine in use, or null when I'm not following one.</summary>
    public Task<Routine?> GetActiveRoutineAsync() => RunAsync(c =>
        c.QueryFirstOrDefaultAsync<Routine?>("SELECT r.Id, r.Name FROM Routine r JOIN Setting s ON s.Key = @ActiveRoutineKey AND CAST(s.Value AS INTEGER) = r.Id", new { ActiveRoutineKey }));

    /// <summary>Follows a routine from now on; null follows none.</summary>
    public Task SetActiveRoutineAsync(long? routineId) => SetSettingAsync(ActiveRoutineKey, routineId?.ToString(CultureInfo.InvariantCulture));

    /// <summary>Every saved setting, e.g. the routine in use and rest countdowns.</summary>
    public Task<Dictionary<string, string>> GetSettingsAsync() => RunAsync(async c =>
        (await c.QueryAsync<(string Key, string Value)>("SELECT Key, Value FROM Setting")).ToDictionary(s => s.Key, s => s.Value));

    /// <summary>Saves a setting; null removes it.</summary>
    public Task SetSettingAsync(string key, string? value) => RunAsync(c => value is null
        ? c.ExecuteAsync("DELETE FROM Setting WHERE Key = @key", new { key })
        : c.ExecuteAsync("INSERT INTO Setting (Key, Value) VALUES (@key, @value) ON CONFLICT (Key) DO UPDATE SET Value = excluded.Value", new { key, value }));

    /// <summary>Reads the saved calorie goal and body details into <paramref name="settings"/>; any not saved are the defaults.</summary>
    public async Task LoadAsync(AppSettings settings)
    {
        var saved = await GetSettingsAsync();
        var defaults = new AppSettings();
        settings.CalorieGoal = int.TryParse(saved.GetValueOrDefault(CalorieGoalKey), CultureInfo.InvariantCulture, out var goal) ? goal : defaults.CalorieGoal;
        settings.Sex = Enum.TryParse<Sex>(saved.GetValueOrDefault(SexKey), out var sex) ? sex : null;
        settings.BirthYear = int.TryParse(saved.GetValueOrDefault(BirthYearKey), CultureInfo.InvariantCulture, out var born) ? born : null;
        settings.HeightCm = double.TryParse(saved.GetValueOrDefault(HeightKey), CultureInfo.InvariantCulture, out var height) ? height : null;
        settings.DailyActivity = Enum.TryParse<DailyActivity>(saved.GetValueOrDefault(DailyActivityKey), out var activity) ? activity : defaults.DailyActivity;
    }

    /// <summary>Saves the calorie goal and body details; an empty one is removed.</summary>
    public Task SaveAsync(AppSettings settings) => Task.WhenAll(
        SetSettingAsync(CalorieGoalKey, settings.CalorieGoal.ToString(CultureInfo.InvariantCulture)),
        SetSettingAsync(SexKey, settings.Sex?.ToString()),
        SetSettingAsync(BirthYearKey, settings.BirthYear?.ToString(CultureInfo.InvariantCulture)),
        SetSettingAsync(HeightKey, settings.HeightCm?.ToString(CultureInfo.InvariantCulture)),
        SetSettingAsync(DailyActivityKey, settings.DailyActivity.ToString()));

    /// <summary>A routine's week, Monday first: each day's session, or null for a rest day.</summary>
    public Task<long?[]> GetRoutineWeekAsync(long routineId) => RunAsync(async c =>
    {
        var week = new long?[7];
        foreach (var (day, templateId) in await c.QueryAsync<(int, long?)>("SELECT Day, TemplateId FROM RoutineDay WHERE RoutineId = @routineId", new { routineId })) week[day] = templateId;
        return week;
    });

    /// <summary>Saves a routine's name and week (Monday first, null for rest days); a new routine gets its Id.</summary>
    public Task SaveRoutineAsync(Routine routine, IReadOnlyList<long?> week) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        if (routine.Id == 0) routine.Id = await c.ExecuteScalarAsync<long>("INSERT INTO Routine (Name) VALUES (@Name); SELECT last_insert_rowid();", routine, transaction);
        else await c.ExecuteAsync("UPDATE Routine SET Name = @Name WHERE Id = @Id", routine, transaction);

        await c.ExecuteAsync("DELETE FROM RoutineDay WHERE RoutineId = @Id", routine, transaction);
        await c.ExecuteAsync("INSERT INTO RoutineDay (RoutineId, Day, TemplateId) VALUES (@RoutineId, @Day, @TemplateId)", week.Select((templateId, day) => new { RoutineId = routine.Id, Day = day, TemplateId = templateId }), transaction);
        await transaction.CommitAsync();
    });

    /// <summary>Deletes a routine; its sessions stay. If it was in use, no routine is.</summary>
    public Task DeleteRoutineAsync(Routine routine) => RunAsync(async c =>
    {
        await c.ExecuteAsync("DELETE FROM Setting WHERE Key = @ActiveRoutineKey AND CAST(Value AS INTEGER) = @Id", new { ActiveRoutineKey, routine.Id });
        await c.ExecuteAsync("DELETE FROM Routine WHERE Id = @Id", routine);
    });

    /// <summary>Every session, whichever routines use them.</summary>
    public Task<List<SessionTemplate>> GetTemplatesAsync() => RunAsync(async c =>
        (await c.QueryAsync<SessionTemplate>("""SELECT Id, Name, "Order", WarmUp, CoolDown FROM SessionTemplate ORDER BY "Order", Id""")).AsList());

    public Task<SessionTemplate?> GetTemplateAsync(long id) => RunAsync(c =>
        c.QueryFirstOrDefaultAsync<SessionTemplate?>("""SELECT Id, Name, "Order", WarmUp, CoolDown FROM SessionTemplate WHERE Id = @id""", new { id }));

    public Task<List<TemplateExercise>> GetTemplateExercisesAsync(long templateId) => RunAsync(async c =>
        (await c.QueryAsync<TemplateExercise>("""SELECT Id, TemplateId, ExerciseId, "Order", PlannedSets, SupersetGroup FROM TemplateExercise WHERE TemplateId = @templateId ORDER BY "Order" """, new { templateId })).AsList());

    /// <summary>Saves a template's name, warm-up and cool-down, and replaces its exercise list. A new template goes last.</summary>
    public Task SaveTemplateAsync(SessionTemplate template, IReadOnlyList<TemplateExercise> entries) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        if (template.Id == 0) template.Id = await c.ExecuteScalarAsync<long>("""INSERT INTO SessionTemplate (Name, "Order", WarmUp, CoolDown) VALUES (@Name, (SELECT COALESCE(MAX("Order"), -1) + 1 FROM SessionTemplate), @WarmUp, @CoolDown); SELECT last_insert_rowid();""", template, transaction);
        else await c.ExecuteAsync("UPDATE SessionTemplate SET Name = @Name, WarmUp = @WarmUp, CoolDown = @CoolDown WHERE Id = @Id", template, transaction);

        foreach (var entry in entries) entry.TemplateId = template.Id;
        await c.ExecuteAsync("DELETE FROM TemplateExercise WHERE TemplateId = @Id", template, transaction);
        await c.ExecuteAsync("""INSERT INTO TemplateExercise (TemplateId, ExerciseId, "Order", PlannedSets, SupersetGroup) VALUES (@TemplateId, @ExerciseId, @Order, @PlannedSets, @SupersetGroup)""", entries, transaction);
        await transaction.CommitAsync();
    });

    public Task<List<Exercise>> GetExercisesAsync() => RunAsync(async c => (await c.QueryAsync<Exercise>("SELECT Id, Name, Measure FROM Exercise ORDER BY Name")).AsList());

    /// <summary>Links to the exercise with the same name if there is one; otherwise creates it with the given measure.</summary>
    public Task<Exercise> GetOrCreateExerciseAsync(string name, ExerciseMeasure measure) => RunAsync(async c =>
    {
        var key = ExerciseNames.Key(name);
        var existing = await c.QueryFirstOrDefaultAsync<Exercise>("SELECT Id, Name, Measure FROM Exercise WHERE NameKey = @key", new { key });
        if (existing is not null) return existing;

        var exercise = new Exercise { Name = ExerciseNames.Clean(name), Measure = measure };
        exercise.Id = await c.ExecuteScalarAsync<long>("INSERT INTO Exercise (Name, NameKey, Measure) VALUES (@Name, @key, @Measure); SELECT last_insert_rowid();", new { exercise.Name, key, exercise.Measure });
        return exercise;
    });

    public Task<List<WorkoutSession>> GetSessionsAsync() => RunAsync(async c =>
        (await c.QueryAsync<WorkoutSession>("SELECT Id, TemplateId, StartedAt, FinishedAt, LoggedAfterward, Improvement, ExercisesCompared, WarmUp, WarmUpEndedAt, CoolDown, CoolDownStartedAt, AverageBpm FROM WorkoutSession ORDER BY Id")).AsList());

    /// <summary>One workout, or null when there's none with that id.</summary>
    public Task<WorkoutSession?> GetSessionAsync(long id) => RunAsync(c =>
        c.QueryFirstOrDefaultAsync<WorkoutSession?>("SELECT Id, TemplateId, StartedAt, FinishedAt, LoggedAfterward, Improvement, ExercisesCompared, WarmUp, WarmUpEndedAt, CoolDown, CoolDownStartedAt, AverageBpm FROM WorkoutSession WHERE Id = @id", new { id }));

    /// <summary>Saves a new session and fills in its Id.</summary>
    public Task StartSessionAsync(WorkoutSession session) => RunAsync(async c =>
        session.Id = await c.ExecuteScalarAsync<long>("INSERT INTO WorkoutSession (TemplateId, StartedAt, WarmUp, CoolDown) VALUES (@TemplateId, @StartedAt, @WarmUp, @CoolDown); SELECT last_insert_rowid();", session));

    public Task FinishSessionAsync(WorkoutSession session) => RunAsync(c => c.ExecuteAsync("UPDATE WorkoutSession SET FinishedAt = @FinishedAt WHERE Id = @Id", session));

    /// <summary>Saves when the warm-up ended and the cool-down started. The session may still be waiting to be saved; its Id is read when this runs.</summary>
    public Task SaveWarmUpAndCoolDownAsync(WorkoutSession session) =>
        RunAsync(c => c.ExecuteAsync("UPDATE WorkoutSession SET WarmUpEndedAt = @WarmUpEndedAt, CoolDownStartedAt = @CoolDownStartedAt WHERE Id = @Id", session));

    public Task SaveImprovementAsync(WorkoutSession session) =>
        RunAsync(c => c.ExecuteAsync("UPDATE WorkoutSession SET Improvement = @Improvement, ExercisesCompared = @ExercisesCompared WHERE Id = @Id", session));

    /// <summary>Removes a session and its sets, e.g. one started by mistake.</summary>
    public Task DeleteSessionAsync(WorkoutSession session) => RunAsync(c => c.ExecuteAsync("DELETE FROM WorkoutSession WHERE Id = @Id", session));

    public Task<List<WorkoutSet>> GetSetsAsync() => RunAsync(async c =>
        (await c.QueryAsync<WorkoutSet>("""SELECT Id, SessionId, ExerciseId, "Order", StartedAt, StoppedAt, Weight, WeightUnit, Reps, DurationSeconds, HitGoal FROM WorkoutSet ORDER BY SessionId, "Order" """)).AsList());

    /// <summary>One workout's sets, in order.</summary>
    public Task<List<WorkoutSet>> GetSessionSetsAsync(long sessionId) => RunAsync(async c =>
        (await c.QueryAsync<WorkoutSet>("""SELECT Id, SessionId, ExerciseId, "Order", StartedAt, StoppedAt, Weight, WeightUnit, Reps, DurationSeconds, HitGoal FROM WorkoutSet WHERE SessionId = @sessionId ORDER BY "Order" """, new { sessionId })).AsList());

    /// <summary>How many sets each workout had of each exercise: enough for Gym's list and trend, without reading every set.</summary>
    public Task<List<(long SessionId, long ExerciseId, int Sets)>> GetExerciseSetCountsAsync() => RunAsync(async c =>
        (await c.QueryAsync<(long SessionId, long ExerciseId, int Sets)>("SELECT SessionId, ExerciseId, COUNT(*) FROM WorkoutSet GROUP BY SessionId, ExerciseId")).AsList());

    /// <summary>Saves a new set and fills in its Id. The session may still be waiting to be saved; its Id is read when this runs.</summary>
    public Task InsertSetAsync(WorkoutSession session, WorkoutSet set) => RunAsync(async c =>
    {
        set.SessionId = session.Id;
        set.Id = await c.ExecuteScalarAsync<long>("""INSERT INTO WorkoutSet (SessionId, ExerciseId, "Order", StartedAt, StoppedAt, Weight, WeightUnit, Reps, DurationSeconds, HitGoal) VALUES (@SessionId, @ExerciseId, @Order, @StartedAt, @StoppedAt, @Weight, @WeightUnit, @Reps, @DurationSeconds, @HitGoal); SELECT last_insert_rowid();""", set);
    });

    /// <summary>Removes a set, e.g. one started on an exercise that was changed before it was done. Its Id is read when this runs.</summary>
    public Task DeleteSetAsync(WorkoutSet set) => RunAsync(c => c.ExecuteAsync("DELETE FROM WorkoutSet WHERE Id = @Id", set));

    /// <summary>A workout's own plan, copied from its template when it started and changed as it went; empty when none was saved.</summary>
    public Task<List<TemplateExercise>> GetSessionPlanAsync(long sessionId) => RunAsync(async c =>
        (await c.QueryAsync<TemplateExercise>("""SELECT ExerciseId, "Order", PlannedSets, SupersetGroup FROM SessionExercise WHERE SessionId = @sessionId ORDER BY "Order" """, new { sessionId })).AsList());

    /// <summary>Replaces a workout's own plan. The session may still be waiting to be saved; its Id is read when this runs.</summary>
    public Task SaveSessionPlanAsync(WorkoutSession session, IReadOnlyList<TemplateExercise> plan) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        await c.ExecuteAsync("DELETE FROM SessionExercise WHERE SessionId = @Id", session, transaction);
        await c.ExecuteAsync("""INSERT INTO SessionExercise (SessionId, ExerciseId, "Order", PlannedSets, SupersetGroup) VALUES (@SessionId, @ExerciseId, @Order, @PlannedSets, @SupersetGroup)""", plan.Select(e => new { SessionId = session.Id, e.ExerciseId, e.Order, e.PlannedSets, e.SupersetGroup }), transaction);
        await transaction.CommitAsync();
    });

    public Task UpdateSetAsync(WorkoutSet set) => RunAsync(c =>
        c.ExecuteAsync("UPDATE WorkoutSet SET StoppedAt = @StoppedAt, Weight = @Weight, WeightUnit = @WeightUnit, Reps = @Reps, DurationSeconds = @DurationSeconds, HitGoal = @HitGoal WHERE Id = @Id", set));

    public Task AddFoodAsync(int calories, DateTimeOffset at) => RunAsync(c => c.ExecuteAsync("INSERT INTO FoodEntry (At, Calories) VALUES (@at, @calories)", new { at, calories }));

    /// <summary>Everything I've logged eating, oldest first.</summary>
    public Task<List<FoodEntry>> GetFoodAsync() => RunAsync(async c => (await c.QueryAsync<FoodEntry>("SELECT Id, At, Calories FROM FoodEntry ORDER BY Id")).AsList());

    /// <summary>Food eaten since a time. Times are text with an offset, so SQL narrows with a day of slack and the exact range is checked in memory.</summary>
    public Task<List<FoodEntry>> GetFoodSinceAsync(DateTimeOffset since) => RunAsync(async c =>
        (await c.QueryAsync<FoodEntry>("SELECT Id, At, Calories FROM FoodEntry WHERE At >= @start ORDER BY Id", new { start = since.AddDays(-1) })).Where(e => e.At >= since).ToList());

    public Task DeleteFoodAsync(FoodEntry entry) => RunAsync(c => c.ExecuteAsync("DELETE FROM FoodEntry WHERE Id = @Id", entry));

    public Task AddBodyWeightAsync(double weight, WeightUnit unit, DateTimeOffset at) => RunAsync(c =>
        c.ExecuteAsync("INSERT INTO BodyWeightEntry (At, Weight, WeightUnit) VALUES (@at, @weight, @unit)", new { at, weight, unit }));

    public Task<List<BodyWeightEntry>> GetBodyWeightsAsync() => RunAsync(async c =>
        (await c.QueryAsync<BodyWeightEntry>("SELECT Id, At, Weight, WeightUnit FROM BodyWeightEntry ORDER BY Id")).AsList());

    /// <summary>Weigh-ins since a time, with a day of slack as for food, and the newest few whenever they were.</summary>
    public Task<List<BodyWeightEntry>> GetBodyWeightsSinceAsync(DateTimeOffset since, int newest) => RunAsync(async c =>
        (await c.QueryAsync<BodyWeightEntry>("SELECT Id, At, Weight, WeightUnit FROM BodyWeightEntry WHERE At >= @start OR Id IN (SELECT Id FROM BodyWeightEntry ORDER BY Id DESC LIMIT @newest) ORDER BY Id", new { start = since.AddDays(-1), newest })).AsList());

    public Task DeleteBodyWeightAsync(BodyWeightEntry entry) => RunAsync(c => c.ExecuteAsync("DELETE FROM BodyWeightEntry WHERE Id = @Id", entry));

    public Task AddHeartRateAsync(IReadOnlyList<HeartRateSample> samples) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        await c.ExecuteAsync("INSERT INTO HeartRateSample (At, Bpm) VALUES (@At, @Bpm)", samples, transaction);
        await transaction.CommitAsync();
    });

    /// <summary>Readings in a stretch of time. Times are text with an offset, so SQL narrows with a day of slack and the exact range is checked in memory.</summary>
    public Task<List<HeartRateSample>> GetHeartRateAsync(DateTimeOffset from, DateTimeOffset to) => RunAsync(c => ReadingsAsync(c, from, to));

    /// <summary>
    /// Works out a workout's average heart rate from <paramref name="from"/> to <paramref name="to"/> (0 without readings), keeps it
    /// with the workout, and returns it. It's one piece of work, so a read started after it sees it.
    /// </summary>
    public Task<double> SaveAverageBpmAsync(WorkoutSession session, DateTimeOffset from, DateTimeOffset to) => RunAsync(async c =>
    {
        var readings = await ReadingsAsync(c, from, to);
        var average = readings.Count == 0 ? 0 : readings.Average(r => r.Bpm);
        await c.ExecuteAsync("UPDATE WorkoutSession SET AverageBpm = @average WHERE Id = @Id", new { average, session.Id });
        return average;
    });

    // GetHeartRateAsync's readings, for work already running.
    private static async Task<List<HeartRateSample>> ReadingsAsync(SqliteConnection c, DateTimeOffset from, DateTimeOffset to) =>
        (await c.QueryAsync<HeartRateSample>("SELECT At, Bpm FROM HeartRateSample WHERE At >= @start AND At <= @end", new { start = from.AddDays(-1), end = to.AddDays(1) })).Where(s => s.At >= from && s.At <= to).ToList();

    /// <summary>Queues work behind everything started before it and runs it on a background thread with its own connection.</summary>
    private Task<T> RunAsync<T>(Func<SqliteConnection, Task<T>> work)
    {
        lock (laneGate)
        {
            // ContinueWith runs whether the previous piece succeeded or failed, so one failure doesn't block the lane.
            var next = lane.ContinueWith(async _ =>
            {
                await using var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                if (!schemaReady)
                {
                    await connection.ExecuteAsync(Schema);
                    if (await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pragma_table_info('SessionTemplate') WHERE name = 'RoutineId'") > 0) await connection.ExecuteAsync(SessionsWithoutRoutine);
                    if (await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pragma_table_info('WorkoutSession') WHERE name = 'Improvement'") == 0) await connection.ExecuteAsync(SessionImprovement);
                    if (await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pragma_table_info('WorkoutSession') WHERE name = 'WarmUp'") == 0) await connection.ExecuteAsync(WarmUpAndCoolDown);
                    if (await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM pragma_table_info('WorkoutSession') WHERE name = 'AverageBpm'") == 0) await connection.ExecuteAsync(SessionHeartRate);
                    schemaReady = true;
                }

                return await work(connection);
            }, CancellationToken.None, TaskContinuationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
            lane = next;
            return next;
        }
    }

    private Task RunAsync(Func<SqliteConnection, Task> work) => RunAsync(async c => { await work(c); return true; });

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) => parameter.Value = value.ToString("O", CultureInfo.InvariantCulture);
    }
}
