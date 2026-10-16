using System.Data;
using System.Globalization;
using AdaptiveWorkout.Core;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AdaptiveWorkout.UI.Storage;

/// <summary>Everything the app records, in one SQLite file on the phone.</summary>
public sealed class WorkoutStore(string databasePath)
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS Routine (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS SessionTemplate (Id INTEGER PRIMARY KEY AUTOINCREMENT, RoutineId INTEGER NOT NULL REFERENCES Routine (Id), Name TEXT NOT NULL, "Order" INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS Exercise (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, NameKey TEXT NOT NULL UNIQUE, Measure INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS TemplateExercise (Id INTEGER PRIMARY KEY AUTOINCREMENT, TemplateId INTEGER NOT NULL REFERENCES SessionTemplate (Id) ON DELETE CASCADE, ExerciseId INTEGER NOT NULL REFERENCES Exercise (Id), "Order" INTEGER NOT NULL, PlannedSets INTEGER, SupersetGroup INTEGER);
        CREATE TABLE IF NOT EXISTS WorkoutSession (Id INTEGER PRIMARY KEY AUTOINCREMENT, TemplateId INTEGER REFERENCES SessionTemplate (Id), StartedAt TEXT NOT NULL, FinishedAt TEXT, LoggedAfterward INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS WorkoutSet (Id INTEGER PRIMARY KEY AUTOINCREMENT, SessionId INTEGER NOT NULL REFERENCES WorkoutSession (Id) ON DELETE CASCADE, ExerciseId INTEGER NOT NULL REFERENCES Exercise (Id), "Order" INTEGER NOT NULL, StartedAt TEXT, StoppedAt TEXT, Weight REAL, WeightUnit INTEGER NOT NULL, Reps INTEGER, DurationSeconds INTEGER, HitGoal INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS FoodEntry (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Calories INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS BodyWeightEntry (Id INTEGER PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL, Weight REAL NOT NULL, WeightUnit INTEGER NOT NULL);
        """;

    private readonly SemaphoreSlim schemaLock = new(1, 1);
    private bool schemaReady;

    static WorkoutStore()
    {
        // SQLite keeps dates as text; Dapper has to be told how to read them back.
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
    }

    public async Task<Routine?> GetRoutineAsync()
    {
        await using var connection = await OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<Routine>("SELECT Id, Name FROM Routine ORDER BY Id LIMIT 1");
    }

    public async Task<Routine> CreateRoutineAsync(string name)
    {
        await using var connection = await OpenAsync();
        var id = await connection.ExecuteScalarAsync<long>("INSERT INTO Routine (Name) VALUES (@name); SELECT last_insert_rowid();", new { name });
        return new Routine { Id = id, Name = name };
    }

    public async Task<IReadOnlyList<SessionTemplate>> GetTemplatesAsync(long routineId)
    {
        await using var connection = await OpenAsync();
        var templates = await connection.QueryAsync<SessionTemplate>("""SELECT Id, RoutineId, Name, "Order" FROM SessionTemplate WHERE RoutineId = @routineId ORDER BY "Order" """, new { routineId });
        return templates.AsList();
    }

    public async Task<SessionTemplate?> GetTemplateAsync(long id)
    {
        await using var connection = await OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<SessionTemplate>("""SELECT Id, RoutineId, Name, "Order" FROM SessionTemplate WHERE Id = @id""", new { id });
    }

    public async Task<IReadOnlyList<TemplateExercise>> GetTemplateExercisesAsync(long templateId)
    {
        await using var connection = await OpenAsync();
        var entries = await connection.QueryAsync<TemplateExercise>("""SELECT Id, TemplateId, ExerciseId, "Order", PlannedSets, SupersetGroup FROM TemplateExercise WHERE TemplateId = @templateId ORDER BY "Order" """, new { templateId });
        return entries.AsList();
    }

    /// <summary>Saves a template's name and replaces its exercise list. A new template goes last in its routine.</summary>
    public async Task<long> SaveTemplateAsync(SessionTemplate template, IReadOnlyList<TemplateExercise> entries)
    {
        await using var connection = await OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        if (template.Id == 0) template.Id = await connection.ExecuteScalarAsync<long>("""INSERT INTO SessionTemplate (RoutineId, Name, "Order") VALUES (@RoutineId, @Name, (SELECT COALESCE(MAX("Order"), -1) + 1 FROM SessionTemplate WHERE RoutineId = @RoutineId)); SELECT last_insert_rowid();""", template, transaction);
        else await connection.ExecuteAsync("UPDATE SessionTemplate SET Name = @Name WHERE Id = @Id", template, transaction);

        foreach (var entry in entries) entry.TemplateId = template.Id;
        await connection.ExecuteAsync("DELETE FROM TemplateExercise WHERE TemplateId = @Id", template, transaction);
        await connection.ExecuteAsync("""INSERT INTO TemplateExercise (TemplateId, ExerciseId, "Order", PlannedSets, SupersetGroup) VALUES (@TemplateId, @ExerciseId, @Order, @PlannedSets, @SupersetGroup)""", entries, transaction);
        await transaction.CommitAsync();
        return template.Id;
    }

    public async Task<IReadOnlyList<Exercise>> GetExercisesAsync()
    {
        await using var connection = await OpenAsync();
        var exercises = await connection.QueryAsync<Exercise>("SELECT Id, Name, Measure FROM Exercise ORDER BY Name");
        return exercises.AsList();
    }

    /// <summary>Links to the exercise with the same name if there is one; otherwise creates it with the given measure.</summary>
    public async Task<Exercise> GetOrCreateExerciseAsync(string name, ExerciseMeasure measure)
    {
        await using var connection = await OpenAsync();
        var key = ExerciseNames.Key(name);
        var existing = await connection.QueryFirstOrDefaultAsync<Exercise>("SELECT Id, Name, Measure FROM Exercise WHERE NameKey = @key", new { key });
        if (existing is not null) return existing;

        var exercise = new Exercise { Name = ExerciseNames.Clean(name), Measure = measure };
        exercise.Id = await connection.ExecuteScalarAsync<long>("INSERT INTO Exercise (Name, NameKey, Measure) VALUES (@Name, @key, @Measure); SELECT last_insert_rowid();", new { exercise.Name, key, exercise.Measure });
        return exercise;
    }

    public async Task<IReadOnlyList<WorkoutSession>> GetSessionsAsync()
    {
        await using var connection = await OpenAsync();
        var sessions = await connection.QueryAsync<WorkoutSession>("SELECT Id, TemplateId, StartedAt, FinishedAt, LoggedAfterward FROM WorkoutSession ORDER BY Id");
        return sessions.AsList();
    }

    public async Task<WorkoutSession?> GetOpenSessionAsync()
    {
        await using var connection = await OpenAsync();
        return await connection.QueryFirstOrDefaultAsync<WorkoutSession>("SELECT Id, TemplateId, StartedAt, FinishedAt, LoggedAfterward FROM WorkoutSession WHERE FinishedAt IS NULL ORDER BY Id DESC LIMIT 1");
    }

    public async Task<WorkoutSession> StartSessionAsync(long? templateId, DateTimeOffset at)
    {
        await using var connection = await OpenAsync();
        var session = new WorkoutSession { TemplateId = templateId, StartedAt = at };
        session.Id = await connection.ExecuteScalarAsync<long>("INSERT INTO WorkoutSession (TemplateId, StartedAt) VALUES (@TemplateId, @StartedAt); SELECT last_insert_rowid();", session);
        return session;
    }

    public async Task FinishSessionAsync(long sessionId, DateTimeOffset at)
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync("UPDATE WorkoutSession SET FinishedAt = @at WHERE Id = @sessionId", new { sessionId, at });
    }

    /// <summary>Removes a session and its sets, e.g. one started by mistake.</summary>
    public async Task DeleteSessionAsync(long sessionId)
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync("DELETE FROM WorkoutSession WHERE Id = @sessionId", new { sessionId });
    }

    public async Task<IReadOnlyList<WorkoutSet>> GetSetsAsync()
    {
        await using var connection = await OpenAsync();
        var sets = await connection.QueryAsync<WorkoutSet>("""SELECT Id, SessionId, ExerciseId, "Order", StartedAt, StoppedAt, Weight, WeightUnit, Reps, DurationSeconds, HitGoal FROM WorkoutSet ORDER BY SessionId, "Order" """);
        return sets.AsList();
    }

    public async Task InsertSetAsync(WorkoutSet set)
    {
        await using var connection = await OpenAsync();
        set.Id = await connection.ExecuteScalarAsync<long>("""INSERT INTO WorkoutSet (SessionId, ExerciseId, "Order", StartedAt, StoppedAt, Weight, WeightUnit, Reps, DurationSeconds, HitGoal) VALUES (@SessionId, @ExerciseId, @Order, @StartedAt, @StoppedAt, @Weight, @WeightUnit, @Reps, @DurationSeconds, @HitGoal); SELECT last_insert_rowid();""", set);
    }

    public async Task UpdateSetAsync(WorkoutSet set)
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync("UPDATE WorkoutSet SET StoppedAt = @StoppedAt, Weight = @Weight, WeightUnit = @WeightUnit, Reps = @Reps, DurationSeconds = @DurationSeconds, HitGoal = @HitGoal WHERE Id = @Id", set);
    }

    public async Task AddFoodAsync(int calories, DateTimeOffset at)
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync("INSERT INTO FoodEntry (At, Calories) VALUES (@at, @calories)", new { at, calories });
    }

    public async Task<IReadOnlyList<FoodEntry>> GetFoodSinceAsync(DateTimeOffset since)
    {
        await using var connection = await OpenAsync();
        var entries = await connection.QueryAsync<FoodEntry>("SELECT Id, At, Calories FROM FoodEntry ORDER BY Id");
        return [.. entries.Where(e => e.At >= since)];
    }

    public async Task AddBodyWeightAsync(double weight, WeightUnit unit, DateTimeOffset at)
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync("INSERT INTO BodyWeightEntry (At, Weight, WeightUnit) VALUES (@at, @weight, @unit)", new { at, weight, unit });
    }

    public async Task<IReadOnlyList<BodyWeightEntry>> GetBodyWeightsAsync()
    {
        await using var connection = await OpenAsync();
        var entries = await connection.QueryAsync<BodyWeightEntry>("SELECT Id, At, Weight, WeightUnit FROM BodyWeightEntry ORDER BY Id");
        return entries.AsList();
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString());
        await connection.OpenAsync();
        if (schemaReady) return connection;

        await schemaLock.WaitAsync();
        try
        {
            if (!schemaReady) await connection.ExecuteAsync(Schema);
            schemaReady = true;
        }
        finally
        {
            schemaLock.Release();
        }

        return connection;
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) => parameter.Value = value.ToString("O", CultureInfo.InvariantCulture);
    }
}
