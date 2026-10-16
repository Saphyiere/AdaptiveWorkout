namespace AdaptiveWorkout.Core;

/// <summary>Which session of a routine comes next.</summary>
public static class Rotation
{
    /// <summary>
    /// The template after the last finished session from this routine, wrapping around; the first when none is finished yet.
    /// Weekdays don't matter: nothing is ever missed, the next session simply waits.
    /// </summary>
    public static SessionTemplate? Next(IReadOnlyList<SessionTemplate> templates, IEnumerable<WorkoutSession> sessions)
    {
        if (templates.Count == 0) return null;

        var ordered = templates.OrderBy(t => t.Order).ToList();
        var fromRoutine = sessions.Where(s => s.FinishedAt is not null && ordered.Any(t => t.Id == s.TemplateId));
        var lastFinished = fromRoutine.MaxBy(s => s.FinishedAt);
        var lastIndex = ordered.FindIndex(t => t.Id == lastFinished?.TemplateId);
        return ordered[(lastIndex + 1) % ordered.Count];
    }
}
