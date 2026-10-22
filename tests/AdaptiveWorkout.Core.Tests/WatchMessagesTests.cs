using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class WatchMessagesTests
{
    private static readonly DateTimeOffset Stopped = new(2026, 10, 18, 18, 30, 15, TimeSpan.FromHours(-4));

    [Fact]
    public void Status_ComesBackTheSame_WithACountdownAndANextSet()
    {
        var rest = new WorkoutStatus(true, "Rest", Stopped, Stopped.AddSeconds(90), "Romanian deadlift");

        Assert.Equal(rest, WatchMessages.DecodeStatus(WatchMessages.EncodeStatus(rest)));
    }

    [Fact]
    public void Time_ComesBackTheSame_ToTheMillisecond()
    {
        var restEndsAt = Stopped.AddSeconds(90).AddMilliseconds(250);

        Assert.Equal(restEndsAt, WatchMessages.DecodeTime(WatchMessages.EncodeTime(restEndsAt)));
    }

    [Fact]
    public void Status_ComesBackTheSame_WithoutACountdownOrANextSet()
    {
        var set = new WorkoutStatus(false, "Bench press", Stopped, null, null);
        var lastRest = new WorkoutStatus(true, "Rest", Stopped, null, null);

        Assert.Equal(set, WatchMessages.DecodeStatus(WatchMessages.EncodeStatus(set)));
        Assert.Equal(lastRest, WatchMessages.DecodeStatus(WatchMessages.EncodeStatus(lastRest)));
    }
}
