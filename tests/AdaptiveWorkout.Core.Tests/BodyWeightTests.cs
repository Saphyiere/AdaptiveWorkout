using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class BodyWeightTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 7, 0, 0, TimeSpan.FromHours(-4));

    private static BodyWeightEntry On(int day, double weight, WeightUnit unit = WeightUnit.Pounds) => new() { At = Start.AddDays(day), Weight = weight, WeightUnit = unit };

    [Fact]
    public void Trend_ReadsTheChangeOffTheLineThroughTheWeighIns_NotTheLastMorning()
    {
        // Down 0.1 lb a day, each weigh-in up to 0.7 lb off the line, and the last morning heavy: the same as the first.
        BodyWeightEntry[] weighIns = [On(0, 199.3), On(2, 200.5), On(4, 199.95), On(6, 199.75), On(8, 198.85), On(10, 198.65), On(12, 198.1), On(14, 199.3)];

        Assert.Equal(-1.4, BodyWeight.Trend(weighIns, Start, Start.AddDays(14), WeightUnit.Pounds)!.Value, 10);
        Assert.Equal(-1.4 / 2.20462, BodyWeight.Trend(weighIns, Start, Start.AddDays(14), WeightUnit.Kilograms)!.Value, 10);
    }

    [Fact]
    public void Trend_IsEmptyWithoutTwoWeighInsAWeekApart_InTheTime()
    {
        BodyWeightEntry[] weighIns = [On(-10, 210), On(0, 200), On(5, 197.5), On(30, 190)];

        Assert.Null(BodyWeight.Trend(weighIns, Start, Start.AddDays(28), WeightUnit.Pounds));
        Assert.Null(BodyWeight.Trend([On(0, 90, WeightUnit.Kilograms)], Start, Start.AddDays(28), WeightUnit.Pounds));

        // With a third weigh-in, half a pound a day; the ones outside the four weeks, off that line, don't count.
        Assert.Equal(-14, BodyWeight.Trend([.. weighIns, On(28, 186)], Start, Start.AddDays(28), WeightUnit.Pounds)!.Value, 10);
    }
}
