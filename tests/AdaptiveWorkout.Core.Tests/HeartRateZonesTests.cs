using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class HeartRateZonesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(119, 1)]
    [InlineData(120, 2)]
    [InlineData(179, 4)]
    [InlineData(180, 5)]
    [InlineData(200, 5)]
    [InlineData(230, 5)]
    public void ZoneFor_UsesTenPercentBandsStartingAtHalfOfMax(int bpm, int expectedZone)
    {
        Assert.Equal(expectedZone, HeartRateZones.ZoneFor(bpm, maxHeartRate: 200));
    }

    [Fact]
    public void ZoneFor_DoesNotRoundUpIntoTheNextZone()
    {
        // 93 / 188 is 49.47% of max, which must stay below zone 1.
        Assert.Equal(0, HeartRateZones.ZoneFor(93, maxHeartRate: 188));
        Assert.Equal(1, HeartRateZones.ZoneFor(94, maxHeartRate: 188));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ZoneFor_RejectsNonPositiveMaxHeartRate(int maxHeartRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HeartRateZones.ZoneFor(120, maxHeartRate));
    }
}
