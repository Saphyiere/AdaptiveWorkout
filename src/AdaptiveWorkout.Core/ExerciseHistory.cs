namespace AdaptiveWorkout.Core;

/// <summary>An exercise's sets from one past session, in order.</summary>
public sealed record PastSession(WorkoutSession Session, IReadOnlyList<WorkoutSet> Sets);

public static class ExerciseHistory
{
    /// <summary>The most recent sessions that had this exercise, newest first, leaving out one session (usually the current one).</summary>
    public static IReadOnlyList<PastSession> Recent(long exerciseId, IEnumerable<WorkoutSession> sessions, IEnumerable<WorkoutSet> sets, long excludeSessionId, int count)
    {
        var relevant = sets.Where(s => s.ExerciseId == exerciseId && s.SessionId != excludeSessionId);
        var bySession = relevant.GroupBy(s => s.SessionId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Order).ToList());
        var newestFirst = sessions.Where(s => bySession.ContainsKey(s.Id)).OrderByDescending(s => s.StartedAt).Take(count);
        return newestFirst.Select(s => new PastSession(s, bySession[s.Id])).ToList();
    }
}
