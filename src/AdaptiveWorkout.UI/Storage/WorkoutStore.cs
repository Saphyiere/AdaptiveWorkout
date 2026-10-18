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
        CREATE TABLE IF NOT EXISTS SessionTemplate (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, "Order" INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS RoutineDay (RoutineId INTEGER NOT NULL REFERENCES Routine (Id) ON DELETE CASCADE, Day INTEGER NOT NULL, TemplateId INTEGER REFERENCES SessionTemplate (Id), PRIMARY KEY (RoutineId, Day));
        CREATE TABLE IF NOT EXISTS Setting (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS Exercise (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, NameKey TEXT NOT NULL UNIQUE, Measure INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS TemplateExercise (Id INTEGER PRIMARY KEY AUTOINCREMENT, TemplateId INTEGER NOT NULL REFERENCES SessionTemplate (Id) ON DELETE CASCADE, ExerciseId INTEGER NOT NULL REFERENCES Exercise (Id), "Order" INTEGER NOT NULL, PlannedSets INTEGER, SupersetGroup INTEGER);
        CREATE TABLE IF NOT EXISTS WorkoutSession (Id INTEGER PRIMARY KEY AUTOINCREMENT, TemplateId INTEGER REFERENCES SessionTemplate (Id), StartedAt TEXT NOT NULL, FinishedAt TEXT, LoggedAfterward INTEGER NOT NULL DEFAULT 0, Improvement REAL, ExercisesCompared INTEGER);
        CREATE TABLE IF NOT EXISTS WorkoutSet (Id INTEGER PRIMARY KEY AUTOINCREMENT, SessionId INTEGER NOT NULL REFERENCES WorkoutSession (Id) ON DELETE CASCADE, ExerciseId INTEGER NOT NULL REFERENCES Exercise (Id), "Order" INTEGER NOT NULL, StartedAt TEXT, StoppedAt TEXT, Weight REAL, WeightUnit INTEGER NOT NULL, Reps INTEGER, DurationSeconds INTEGER, HitGoal INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS FoodEntry (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Calories INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS BodyWeightEntry (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Weight REAL NOT NULL, WeightUnit INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS HeartRateSample (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Bpm INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS SessionExercise (Id INTEGER PRIMARY KEY AUTOINCREMENT, SessionId INTEGER NOT NULL REFERENCES WorkoutSession (Id) ON DELETE CASCADE, ExerciseId INTEGER NOT NULL REFERENCES Exercise (Id), "Order" INTEGER NOT NULL, PlannedSets INTEGER, SupersetGroup INTEGER);
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

    private const string ActiveRoutineKey = "ActiveRoutineId";

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
        (await c.QueryAsync<SessionTemplate>("""SELECT Id, Name, "Order" FROM SessionTemplate ORDER BY "Order", Id""")).AsList());

    public Task<SessionTemplate?> GetTemplateAsync(long id) => RunAsync(c =>
        c.QueryFirstOrDefaultAsync<SessionTemplate?>("""SELECT Id, Name, "Order" FROM SessionTemplate WHERE Id = @id""", new { id }));

    public Task<List<TemplateExercise>> GetTemplateExercisesAsync(long templateId) => RunAsync(async c =>
        (await c.QueryAsync<TemplateExercise>("""SELECT Id, TemplateId, ExerciseId, "Order", PlannedSets, SupersetGroup FROM TemplateExercise WHERE TemplateId = @templateId ORDER BY "Order" """, new { templateId })).AsList());

    /// <summary>Saves a template's name and replaces its exercise list. A new template goes last.</summary>
    public Task SaveTemplateAsync(SessionTemplate template, IReadOnlyList<TemplateExercise> entries) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        if (template.Id == 0) template.Id = await c.ExecuteScalarAsync<long>("""INSERT INTO SessionTemplate (Name, "Order") VALUES (@Name, (SELECT COALESCE(MAX("Order"), -1) + 1 FROM SessionTemplate)); SELECT last_insert_rowid();""", template, transaction);
        else await c.ExecuteAsync("UPDATE SessionTemplate SET Name = @Name WHERE Id = @Id", template, transaction);

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
        (await c.QueryAsync<WorkoutSession>("SELECT Id, TemplateId, StartedAt, FinishedAt, LoggedAfterward, Improvement, ExercisesCompared FROM WorkoutSession ORDER BY Id")).AsList());

    /// <summary>Saves a new session and fills in its Id.</summary>
    public Task StartSessionAsync(WorkoutSession session) => RunAsync(async c =>
        session.Id = await c.ExecuteScalarAsync<long>("INSERT INTO WorkoutSession (TemplateId, StartedAt) VALUES (@TemplateId, @StartedAt); SELECT last_insert_rowid();", session));

    public Task FinishSessionAsync(WorkoutSession session) => RunAsync(c => c.ExecuteAsync("UPDATE WorkoutSession SET FinishedAt = @FinishedAt WHERE Id = @Id", session));

    public Task SaveImprovementAsync(WorkoutSession session) =>
        RunAsync(c => c.ExecuteAsync("UPDATE WorkoutSession SET Improvement = @Improvement, ExercisesCompared = @ExercisesCompared WHERE Id = @Id", session));

    /// <summary>Removes a session and its sets, e.g. one started by mistake.</summary>
    public Task DeleteSessionAsync(WorkoutSession session) => RunAsync(c => c.ExecuteAsync("DELETE FROM WorkoutSession WHERE Id = @Id", session));

    public Task<List<WorkoutSet>> GetSetsAsync() => RunAsync(async c =>
        (await c.QueryAsync<WorkoutSet>("""SELECT Id, SessionId, ExerciseId, "Order", StartedAt, StoppedAt, Weight, WeightUnit, Reps, DurationSeconds, HitGoal FROM WorkoutSet ORDER BY SessionId, "Order" """)).AsList());

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

    public Task<List<FoodEntry>> GetFoodSinceAsync(DateTimeOffset since) => RunAsync(async c =>
        (await c.QueryAsync<FoodEntry>("SELECT Id, At, Calories FROM FoodEntry ORDER BY Id")).Where(e => e.At >= since).ToList());

    public Task DeleteFoodAsync(FoodEntry entry) => RunAsync(c => c.ExecuteAsync("DELETE FROM FoodEntry WHERE Id = @Id", entry));

    public Task AddBodyWeightAsync(double weight, WeightUnit unit, DateTimeOffset at) => RunAsync(c =>
        c.ExecuteAsync("INSERT INTO BodyWeightEntry (At, Weight, WeightUnit) VALUES (@at, @weight, @unit)", new { at, weight, unit }));

    public Task<List<BodyWeightEntry>> GetBodyWeightsAsync() => RunAsync(async c =>
        (await c.QueryAsync<BodyWeightEntry>("SELECT Id, At, Weight, WeightUnit FROM BodyWeightEntry ORDER BY Id")).AsList());

    public Task DeleteBodyWeightAsync(BodyWeightEntry entry) => RunAsync(c => c.ExecuteAsync("DELETE FROM BodyWeightEntry WHERE Id = @Id", entry));

    public Task AddHeartRateAsync(IReadOnlyList<HeartRateSample> samples) => RunAsync(async c =>
    {
        await using var transaction = (SqliteTransaction)await c.BeginTransactionAsync();
        await c.ExecuteAsync("INSERT INTO HeartRateSample (At, Bpm) VALUES (@At, @Bpm)", samples, transaction);
        await transaction.CommitAsync();
    });

    /// <summary>Readings in a stretch of time. Times are text with an offset, so SQL narrows with a day of slack and the exact range is checked in memory.</summary>
    public Task<List<HeartRateSample>> GetHeartRateAsync(DateTimeOffset from, DateTimeOffset to) => RunAsync(async c =>
        (await c.QueryAsync<HeartRateSample>("SELECT At, Bpm FROM HeartRateSample WHERE At >= @start AND At <= @end", new { start = from.AddDays(-1), end = to.AddDays(1) })).Where(s => s.At >= from && s.At <= to).ToList());

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
