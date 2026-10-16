namespace AdaptiveWorkout.Core;

/// <summary>Am I showing up? A gym day is any day with a session, a walk included.</summary>
public static class Consistency
{
    /// <summary>The Monday that starts the week holding <paramref name="day"/>.</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

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
}
