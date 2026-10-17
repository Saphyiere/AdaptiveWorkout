using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class ConsistencyTests
{
    private static readonly DateOnly Friday = new(2026, 10, 16);

    [Fact]
    public void Week_MarksMondayToSundayOfTheGivenWeek()
    {
        DateOnly[] gymDays = [new(2026, 10, 11), new(2026, 10, 12), new(2026, 10, 14), new(2026, 10, 15), new(2026, 10, 19)];

        Assert.Equal([true, false, true, true, false, false, false], Consistency.Week(gymDays, Friday));
    }

    [Fact]
    public void WeekStreak_CountsThisWeekOnlyOnceItHasAGymDay()
    {
        DateOnly[] lastThreeWeeks = [new(2026, 9, 22), new(2026, 9, 29), new(2026, 10, 9)];

        Assert.Equal(3, Consistency.WeekStreak(lastThreeWeeks, Friday));
        Assert.Equal(4, Consistency.WeekStreak([.. lastThreeWeeks, new(2026, 10, 12)], Friday));
    }

    [Fact]
    public void WeekStreak_BreaksOnAWeekWithoutAGymDay()
    {
        DateOnly[] gymDays = [new(2026, 9, 21), new(2026, 9, 28), new(2026, 10, 13)];

        Assert.Equal(1, Consistency.WeekStreak(gymDays, Friday));
        Assert.Equal(0, Consistency.WeekStreak([new(2026, 9, 21)], Friday));
    }

    // Monday to Saturday each have a session; Sunday is a rest day.
    private static readonly long?[] SixDays = [1, 2, 3, 4, 5, 6, null];

    private static DateOnly[] Days(params int[] octoberDays) => [.. octoberDays.Select(d => new DateOnly(2026, 10, d))];

    [Fact]
    public void DayStreak_RestDaysNeverBreakIt()
    {
        // Mon 5 to Sat 10, rest on Sunday 11, then Mon 12 to Thu 15. Friday 16 isn't over yet.
        var gymDays = Days(5, 6, 7, 8, 9, 10, 12, 13, 14, 15);

        Assert.Equal(10, Consistency.DayStreak(gymDays, SixDays, Friday));
        Assert.Equal(11, Consistency.DayStreak([.. gymDays, Friday], SixDays, Friday));
        Assert.Equal(6, Consistency.DayStreak(Days(5, 6, 7, 8, 9, 10), SixDays, new DateOnly(2026, 10, 11)));
    }

    [Fact]
    public void DayStreak_ASkippedPlannedDayEndsIt_AWorkoutOnARestDayCounts()
    {
        // Wednesday 14 was skipped.
        Assert.Equal(1, Consistency.DayStreak(Days(12, 13, 15), SixDays, Friday));

        // Sunday 11 was a rest day I worked out anyway; Friday 9 was skipped.
        Assert.Equal(6, Consistency.DayStreak(Days(10, 11, 12, 13, 14, 15), SixDays, Friday));
    }

    [Fact]
    public void DayStreak_WithoutAnyPlannedDays_IsZero()
    {
        Assert.Equal(0, Consistency.DayStreak(Days(14, 15), [null, null, null, null, null, null, null], Friday));
    }
}
