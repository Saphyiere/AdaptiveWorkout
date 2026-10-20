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

/// <summary>A named week of sessions, e.g. "Six-day split": each day has a session or is a rest day. Swap routines any time, or follow none.</summary>
public sealed class Routine
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>A named list of exercises, e.g. "Back day". Any number of routines can use it, on any days.</summary>
public sealed class SessionTemplate
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int Order { get; set; }

    /// <summary>Its workouts start with a warm-up: timed, with heart rate, until the first exercise starts.</summary>
    public bool WarmUp { get; set; }

    /// <summary>Its workouts end with a cool-down: timed, with heart rate, from after the last exercise until Finish.</summary>
    public bool CoolDown { get; set; }
}

/// <summary>One exercise in a plan, in order: a session template's, or one workout's own copy of it.</summary>
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

    /// <summary>It starts with a warm-up, copied from its session at Start, that runs until <see cref="WarmUpEndedAt"/>.</summary>
    public bool WarmUp { get; set; }

    /// <summary>When the warm-up ended, as the first exercise started. Empty while warming up, or without a warm-up.</summary>
    public DateTimeOffset? WarmUpEndedAt { get; set; }

    /// <summary>It ends with a cool-down, copied from its session at Start, that runs from <see cref="CoolDownStartedAt"/> until Finish.</summary>
    public bool CoolDown { get; set; }

    /// <summary>When the cool-down started. Empty until then, or without a cool-down.</summary>
    public DateTimeOffset? CoolDownStartedAt { get; set; }

    /// <summary>
    /// How this workout compared with the last time of each of its exercises, worked out once after Finish (see
    /// <see cref="Progress"/>): 0.03 is 3% better. Empty when none of its exercises had a last time.
    /// </summary>
    public double? Improvement { get; set; }

    /// <summary>How many of its exercises had a last time to compare with; empty until worked out.</summary>
    public int? ExercisesCompared { get; set; }
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

/// <summary>One heart rate reading from the watch. Sessions, sets and rests find theirs by time.</summary>
public sealed class HeartRateSample
{
    public DateTimeOffset At { get; set; }
    public int Bpm { get; set; }
}

public sealed class AppSettings
{
    public int CalorieGoal { get; set; } = 1800;
    public int Maintenance { get; set; } = 2200;
    public WeightUnit Unit { get; set; } = WeightUnit.Pounds;

    /// <summary>How much heavier the suggestion gets after a hit goal, in the set's own unit.</summary>
    public double HitGoalStep { get; set; } = 5;
}
