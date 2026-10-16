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
}
