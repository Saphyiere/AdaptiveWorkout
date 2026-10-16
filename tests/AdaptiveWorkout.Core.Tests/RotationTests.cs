using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class RotationTests
{
    private static readonly SessionTemplate Push = new() { Id = 1, Name = "Push", Order = 0 };
    private static readonly SessionTemplate Back = new() { Id = 2, Name = "Back", Order = 1 };
    private static readonly SessionTemplate Legs = new() { Id = 3, Name = "Legs", Order = 2 };
    private static readonly SessionTemplate[] Templates = [Legs, Push, Back];

    private static WorkoutSession Finished(long? templateId, int day) => new() { TemplateId = templateId, StartedAt = Day(day), FinishedAt = Day(day).AddHours(1) };

    private static DateTimeOffset Day(int day) => new(2026, 10, day, 18, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void Next_StartsWithTheFirstTemplateWhenNothingIsFinished()
    {
        Assert.Same(Push, Rotation.Next(Templates, []));
    }

    [Fact]
    public void Next_FollowsTheLastFinishedSessionAndWrapsAround()
    {
        Assert.Same(Back, Rotation.Next(Templates, [Finished(3, 10), Finished(1, 12)]));
        Assert.Same(Push, Rotation.Next(Templates, [Finished(1, 10), Finished(3, 12)]));
    }

    [Fact]
    public void Next_IgnoresJustTrackingAndUnfinishedSessions()
    {
        var walk = Finished(null, 14);
        var abandoned = new WorkoutSession { TemplateId = 3, StartedAt = Day(15) };

        Assert.Same(Back, Rotation.Next(Templates, [Finished(1, 12), walk, abandoned]));
    }
}
