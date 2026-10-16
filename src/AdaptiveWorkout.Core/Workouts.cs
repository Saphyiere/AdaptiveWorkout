namespace AdaptiveWorkout.Core;

public enum ExerciseMeasure
{
    Weight = 0,
    Time = 1,
}

public enum WeightUnit
{
    Pounds = 0,
    Kilograms = 1,
}

/// <summary>A named rotation of session templates, e.g. "Standard workout".</summary>
public sealed class Routine
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>One named day in a routine, e.g. "Back day".</summary>
public sealed class SessionTemplate
{
    public long Id { get; set; }
    public long RoutineId { get; set; }
    public string Name { get; set; } = "";
    public int Order { get; set; }
}

/// <summary>One exercise in a session template, in order.</summary>
public sealed class TemplateExercise
{
    public long Id { get; set; }
    public long TemplateId { get; set; }
    public long ExerciseId { get; set; }
    public int Order { get; set; }

    /// <summary>Sets to do (rounds, for a superset). Empty means as many as last time.</summary>
    public int? PlannedSets { get; set; }

    /// <summary>Neighboring entries with the same group number are one superset; the first entry's planned sets count for all.</summary>
    public int? SupersetGroup { get; set; }
}

/// <summary>An exercise is whatever name I typed; variations are separate names.</summary>
public sealed class Exercise
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public ExerciseMeasure Measure { get; set; }
}

/// <summary>One actual workout, from tapping Start to Finish workout.</summary>
public sealed class WorkoutSession
{
    public long Id { get; set; }

    /// <summary>Empty when just tracking.</summary>
    public long? TemplateId { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public bool LoggedAfterward { get; set; }
}

/// <summary>One bout of an exercise. Rest is the gap between sets and is never stored.</summary>
public sealed class WorkoutSet
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public long ExerciseId { get; set; }
    public int Order { get; set; }

    /// <summary>Empty when logged afterward.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Empty while the set is running, or when logged afterward.</summary>
    public DateTimeOffset? StoppedAt { get; set; }

    /// <summary>Empty means bodyweight; negative means assisted.</summary>
    public double? Weight { get; set; }

    public WeightUnit WeightUnit { get; set; }
    public int? Reps { get; set; }

    /// <summary>Time exercises: the stopwatch, or a typed duration when logged afterward.</summary>
    public int? DurationSeconds { get; set; }

    public bool HitGoal { get; set; }
}

public sealed class FoodEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public int Calories { get; set; }
}

public sealed class BodyWeightEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public double Weight { get; set; }
    public WeightUnit WeightUnit { get; set; }
}

public sealed class AppSettings
{
    public int CalorieGoal { get; set; } = 1800;
    public int Maintenance { get; set; } = 2200;
    public WeightUnit Unit { get; set; } = WeightUnit.Pounds;

    /// <summary>How much heavier the suggestion gets after a hit goal, in the set's own unit.</summary>
    public double HitGoalStep { get; set; } = 5;
}
