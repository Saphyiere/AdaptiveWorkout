using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class CaloriesTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-4);
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static readonly AppSettings Him = new() { Sex = Sex.Male, BirthYear = 1996, HeightCm = 180, Activity = ActivityLevel.Moderate };

    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) => new(day.ToDateTime(new TimeOnly(hour, minute)), Offset);

    private static BodyWeightEntry WeighIn(DateOnly day, double weight, WeightUnit unit = WeightUnit.Kilograms) => new() { At = At(day, 7), Weight = weight, WeightUnit = unit };

    [Fact]
    public void Maintenance_IsMifflinStJeorTimesActivity_InKilogramsWhateverTheWeighIn()
    {
        // 10 × 80 + 6.25 × 180 − 5 × 30 + 5 = 1,780 resting; moderately active, × 1.55.
        Assert.Equal(2759, Calories.Maintenance(Him, WeighIn(Monday, 80), Monday));

        // 10 × 60 + 6.25 × 165 − 5 × 40 − 161 = 1,270.25; sitting most of the day, × 1.2. The weigh-in is 60 kg in pounds.
        var her = new AppSettings { Sex = Sex.Female, BirthYear = 1986, HeightCm = 165, Activity = ActivityLevel.Sedentary };
        Assert.Equal(1524, Calories.Maintenance(her, WeighIn(Monday, 132.2772, WeightUnit.Pounds), Monday));
    }

    [Fact]
    public void Maintenance_IsEmptyUntilEveryDetailAndAWeighInAreThere()
    {
        Assert.Null(Calories.Maintenance(Him, null, Monday));
        Assert.Null(Calories.Maintenance(new AppSettings { Sex = Sex.Male, BirthYear = 1996 }, WeighIn(Monday, 80), Monday));
        Assert.Null(Calories.Maintenance(new AppSettings { BirthYear = 1996, HeightCm = 180 }, WeighIn(Monday, 80), Monday));
    }

    [Fact]
    public void Days_AddUpEachDaysFood_AndLeaveDaysWithNoneEmpty()
    {
        FoodEntry Ate(DateOnly day, int hour, int calories, int minute = 0) => new() { At = At(day, hour, minute), Calories = calories };
        FoodEntry[] food = [Ate(Monday, 8, 500), Ate(Monday, 23, 700, 30), Ate(Monday.AddDays(2), 12, 1900), Ate(Monday.AddDays(-1), 20, 300)];

        var days = Calories.Days(food, [], new AppSettings(), Monday, Monday.AddDays(2));

        Assert.Equal([Monday, Monday.AddDays(1), Monday.AddDays(2)], days.Select(d => d.Day));
        Assert.Equal([1200, null, 1900], days.Select(d => d.Eaten));
        Assert.All(days, d => Assert.Null(d.Maintenance));
    }

    [Fact]
    public void Days_TakeMaintenanceFromTheWeighInBeforeTheDayWasOut_ElseTheFirstOneAfter()
    {
        BodyWeightEntry[] weighIns = [WeighIn(Monday.AddDays(1), 80), WeighIn(Monday.AddDays(3), 70)];

        var days = Calories.Days([], weighIns, Him, Monday, Monday.AddDays(3));

        // 80 kg from Monday (the first weigh-in, the day after) through Wednesday; 70 kg is 100 resting calories less, × 1.55.
        Assert.Equal([2759, 2759, 2759, 2604], days.Select(d => d.Maintenance));
    }

    [Fact]
    public void Span_AveragesTheLoggedDays_AndCountsTheRestAsAverageForTheWeight()
    {
        CalorieDay[] week =
        [
            new(Monday, 1800, 2300), new(Monday.AddDays(1), 2000, 2300), new(Monday.AddDays(2), null, 2300), new(Monday.AddDays(3), 1700, 2300),
            new(Monday.AddDays(4), 1500, 2300), new(Monday.AddDays(5), null, 2300), new(Monday.AddDays(6), 2000, 2300),
        ];

        var span = Calories.Span(week);

        // Five logged days, 1,800 on average, 500 under maintenance: 3,500 calories a week is a pound, or 3500 / 7700 kg.
        Assert.Equal((7, 5), (span.Days, span.Logged));
        Assert.Equal(1800, span.AverageEaten);
        Assert.Equal(500, span.AverageDeficit);
        Assert.Equal(-1.0, span.WeightChange(WeightUnit.Pounds)!.Value, 10);
        Assert.Equal(-3500 / 7700.0, span.WeightChange(WeightUnit.Kilograms)!.Value, 10);
    }

    [Fact]
    public void Span_HasNoAveragesWithNothingLogged_AndNoWeightWithoutMaintenance()
    {
        Assert.Equal(new CalorieSpan(2, 0, null, null), Calories.Span([new(Monday, null, 2300), new(Monday.AddDays(1), null, 2300)]));

        var unknown = Calories.Span([new(Monday, 1800, null)]);
        Assert.Equal(1800, unknown.AverageEaten);
        Assert.Null(unknown.WeightChange(WeightUnit.Pounds));
    }
}
