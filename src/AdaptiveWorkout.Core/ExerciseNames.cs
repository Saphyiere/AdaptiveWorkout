namespace AdaptiveWorkout.Core;

/// <summary>Typing a name I've used before links to that exercise.</summary>
public static class ExerciseNames
{
    /// <summary>The name as it should be stored: trimmed, single spaces.</summary>
    public static string Clean(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>What two names match on: cleaned and case-insensitive, so "Flat  chest press" links to "flat chest press".</summary>
    public static string Key(string name) => Clean(name).ToUpperInvariant();
}
