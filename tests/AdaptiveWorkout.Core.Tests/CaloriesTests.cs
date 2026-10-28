using AdaptiveWorkout.Core;

namespace AdaptiveWorkout.Core.Tests;

public class CaloriesTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-4);
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private static readonly AppSettings Him = new() { Sex = Sex.Male, BirthYear = 1996, HeightCm = 180, DailyActivity = DailyActivity.MostlyOnFeet };

    private static readonly Dictionary<DateOnly, int> NoWorkouts = [];

    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) => new(day.ToDateTime(new TimeOnly(hour, minute)), Offset);

    private static BodyWeightEntry WeighIn(DateOnly day, double weight, WeightUnit unit = WeightUnit.Kilograms) => new() { At = At(day, 7), Weight = weight, WeightUnit = unit };

    [Fact]
    public void Maintenance_IsMifflinStJeorTimesActivity_InKilogramsWhateverTheWeighIn()
    {
        // 10 × 80 + 6.25 × 180 − 5 × 30 + 5 = 1,780 resting; on his feet most of the day, × 1.55.
        Assert.Equal(2759, Calories.Maintenance(Him, WeighIn(Monday, 80), Monday));

        // 10 × 60 + 6.25 × 165 − 5 × 40 − 161 = 1,270.25; sitting most of the day, × 1.2. The weigh-in is 60 kg in pounds.
        var her = new AppSettings { Sex = Sex.Female, BirthYear = 1986, HeightCm = 165, DailyActivity = DailyActivity.Sitting };
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

        var days = Calories.Days(food, [], new AppSettings(), Monday, Monday.AddDays(2), NoWorkouts);

        Assert.Equal([Monday, Monday.AddDays(1), Monday.AddDays(2)], days.Select(d => d.Day));
        Assert.Equal([1200, null, 1900], days.Select(d => d.Eaten));
        Assert.All(days, d => Assert.Null(d.Maintenance));
    }

    [Fact]
    public void Days_TakeMaintenanceFromTheWeighInBeforeTheDayWasOut_ElseTheFirstOneAfter()
    {
        BodyWeightEntry[] weighIns = [WeighIn(Monday.AddDays(1), 80), WeighIn(Monday.AddDays(3), 70)];

        var days = Calories.Days([], weighIns, Him, Monday, Monday.AddDays(3), NoWorkouts);

        // 80 kg from Monday (the first weigh-in, the day after) through Wednesday; 70 kg is 100 resting calories less, × 1.55.
        Assert.Equal([2759, 2759, 2759, 2604], days.Select(d => d.Maintenance));
    }

    [Fact]
    public void Days_AddWhatEachDaysWorkoutsBurned_ToItsMaintenance()
    {
        FoodEntry[] food = [new() { At = At(Monday, 12), Calories = 2500 }];

        var days = Calories.Days(food, [WeighIn(Monday, 80)], Him, Monday, Monday.AddDays(1), new Dictionary<DateOnly, int> { [Monday] = 300 });

        Assert.Equal([(3059, 300), (2759, 0)], days.Select(d => (d.Maintenance!.Value, d.Workouts)));
        Assert.Equal(559, days[0].Deficit);
    }

    [Fact]
    public void Burned_IsTheTimeAboveRestingAtThreeAndAHalfMets_WithoutAHeartRateOrAnAge()
    {
        var hour = TimeSpan.FromHours(1);

        // (3.5 − 1) METs × 80 kg × 1 hour; without a weigh-in there's no telling.
        Assert.Equal(200, Calories.Burned(hour, null, true, Him, WeighIn(Monday, 80), Monday));
        Assert.Equal(200, Calories.Burned(hour, 150, true, new AppSettings(), WeighIn(Monday, 80), Monday));
        Assert.Equal(100, Calories.Burned(hour / 2, 0, false, Him, WeighIn(Monday, 176.37, WeightUnit.Pounds), Monday));
        Assert.Null(Calories.Burned(hour, 150, true, Him, null, Monday));
    }

    [Fact]
    public void Burned_ClimbsWithTheHeartRate_ToVigorousLifting_OrOnForATimedWorkout()
    {
        int? Burned(double share, bool lifting) => Calories.Burned(TimeSpan.FromHours(1), share * 187, lifting, Him, WeighIn(Monday, 80), Monday);

        // His maximum is 208 − 0.7 × 30 = 187. Up to 64% of it, a usual workout; at 77%, vigorous lifting (6 METs), and no more for
        // lifting; a timed workout goes on to 8.8 METs at 96%.
        Assert.Equal([200, 200, 300, 400, 400], new[] { 0.5, 0.64, 0.705, 0.77, 0.9 }.Select(share => Burned(share, true)));
        Assert.Equal([300, 400, 553, 624, 624], new[] { 0.705, 0.77, 0.9, 0.96, 1.1 }.Select(share => Burned(share, false)));
    }

    [Fact]
    public void WorkoutTime_EndsAtTheLastSet_UnlessACoolDownRanToFinish()
    {
        var session = new WorkoutSession { StartedAt = At(Monday, 18), FinishedAt = At(Monday, 21) };
        WorkoutSet[] sets = [new() { StoppedAt = At(Monday, 18, 20) }, new() { StoppedAt = At(Monday, 18, 50) }, new()];

        // Left open after the last set: the hours until Finish don't count.
        Assert.Equal((At(Monday, 18), At(Monday, 18, 50)), Calories.WorkoutTime(session, sets));
        Assert.Equal((At(Monday, 18), At(Monday, 21)), Calories.WorkoutTime(session, []));

        session.CoolDownStartedAt = At(Monday, 18, 52);
        Assert.Equal((At(Monday, 18), At(Monday, 21)), Calories.WorkoutTime(session, sets));
    }

    [Fact]
    public void BurnedByDay_AddsUpEachDaysFinishedWorkouts_AtTheWeighInBeforeEach()
    {
        const long Bench = 1, Walk = 2;
        var exercises = new Dictionary<long, Exercise> { [Bench] = new() { Id = Bench, Measure = ExerciseMeasure.Weight }, [Walk] = new() { Id = Walk, Measure = ExerciseMeasure.Time } };
        WorkoutSession Workout(long id, DateOnly day, int hour, int minutes) => new() { Id = id, StartedAt = At(day, hour), FinishedAt = At(day, hour).AddMinutes(minutes) };
        WorkoutSession[] sessions =
        [
            Workout(1, Monday, 7, 30), Workout(2, Monday, 18, 60), Workout(3, Monday.AddDays(2), 18, 60),
            new() { Id = 4, StartedAt = At(Monday.AddDays(2), 20) },
        ];
        WorkoutSet[] sets =
        [
            new() { SessionId = 1, ExerciseId = Walk, StoppedAt = At(Monday, 7, 30) }, new() { SessionId = 2, ExerciseId = Bench, StoppedAt = At(Monday, 19) },
            new() { SessionId = 3, ExerciseId = Bench, StoppedAt = At(Monday.AddDays(2), 19) },
        ];

        var burned = Calories.BurnedByDay(sessions, sets, exercises, [WeighIn(Monday.AddDays(-1), 80), WeighIn(Monday.AddDays(1), 70)], Him);

        // Monday: a half-hour walk and an hour of lifting at 80 kg, 100 + 200. Wednesday: an hour at 70 kg. The open one isn't done.
        Assert.Equal(new Dictionary<DateOnly, int> { [Monday] = 300, [Monday.AddDays(2)] = 175 }, burned);
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
