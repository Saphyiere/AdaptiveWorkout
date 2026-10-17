namespace AdaptiveWorkout.Core;

/// <summary>Am I showing up? A gym day is any day with a session, a walk included.</summary>
public static class Consistency
{
    /// <summary>The Monday that starts the week holding <paramref name="day"/>.</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-Schedule.DayIndex(day));

    /// <summary>Monday to Sunday of the week holding <paramref name="day"/>: which days were gym days.</summary>
    public static bool[] Week(IEnumerable<DateOnly> gymDays, DateOnly day)
    {
        var monday = WeekStart(day);
        var days = gymDays.ToHashSet();
        return [.. Enumerable.Range(0, 7).Select(i => days.Contains(monday.AddDays(i)))];
    }

    /// <summary>Weeks in a row with at least one gym day. This week only counts once it has one, so the streak never breaks mid-week.</summary>
    public static int WeekStreak(IEnumerable<DateOnly> gymDays, DateOnly today)
    {
        var activeWeeks = gymDays.Select(WeekStart).ToHashSet();
        var monday = WeekStart(today);
        if (!activeWeeks.Contains(monday)) monday = monday.AddDays(-7);

        var streak = 0;
        while (activeWeeks.Contains(monday)) { streak++; monday = monday.AddDays(-7); }
        return streak;
    }

    /// <summary>
    /// With a routine: gym days in a row, counting back from today. A planned day I skipped ends it; rest days never do,
    /// and a workout on one still counts. Today only counts once I've been, so the streak never breaks before the day is over.
    /// </summary>
    /// <param name="week">The routine's sessions, Monday first; null days are rest days.</param>
    public static int DayStreak(IEnumerable<DateOnly> gymDays, IReadOnlyList<long?> week, DateOnly today)
    {
        if (week.All(d => d is null)) return 0;

        var went = gymDays.ToHashSet();
        var streak = 0;
        for (var day = today; ; day = day.AddDays(-1))
        {
            if (went.Contains(day)) streak++;
            else if (day != today && Schedule.On(week, day) is not null) return streak;
        }
    }
}
