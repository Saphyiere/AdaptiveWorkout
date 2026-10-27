namespace AdaptiveWorkout.Core;

/// <summary>One day's food: the calories I logged eating (empty on a day I logged none), and maintenance that day (empty until it can be worked out).</summary>
public sealed record CalorieDay(DateOnly Day, int? Eaten, int? Maintenance)
{
    /// <summary>How far under maintenance I ate, negative when over. Empty unless both are known.</summary>
    public int? Deficit => Eaten is int eaten && Maintenance is int maintenance ? maintenance - eaten : null;
}

/// <summary>
/// Some days together, e.g. a week: how many there were, how many I logged, and what I ate and how far under maintenance, on average
/// over the days I logged. A day with nothing logged is left out of the averages: it's a day I didn't log, not a day I didn't eat.
/// </summary>
public sealed record CalorieSpan(int Days, int Logged, double? AverageEaten, double? AverageDeficit)
{
    /// <summary>
    /// What the deficit should do to my weight over all the days, in pounds or kilograms, counting the days I didn't log as average
    /// ones: −1.2 is 1.2 lighter. Empty without maintenance.
    /// </summary>
    public double? WeightChange(WeightUnit unit) =>
        AverageDeficit is double deficit ? -deficit * Days / (unit == WeightUnit.Kilograms ? Calories.PerKilogram : Calories.PerPound) : null;
}

/// <summary>Am I eating to my goal, and what should it do to my weight? What I ate is what I log; maintenance comes from my body.</summary>
public static class Calories
{
    /// <summary>About what a pound of body weight is worth, in calories.</summary>
    public const double PerPound = 3500;

    /// <summary>About what a kilogram of body weight is worth, in calories.</summary>
    public const double PerKilogram = 7700;

    /// <summary>
    /// Calories a day that would keep my weight where it is: resting calories by the Mifflin-St Jeor formula (10 × kg + 6.25 × cm
    /// − 5 × age, then +5 for a man or −161 for a woman) times how active I am. Empty without my sex, birth year, height or a weigh-in.
    /// </summary>
    public static int? Maintenance(AppSettings settings, BodyWeightEntry? weighIn, DateOnly day)
    {
        if (settings is not { Sex: { } sex, BirthYear: int born, HeightCm: double height } || weighIn is null) return null;

        var resting = 10 * WorkoutStats.Kilograms(weighIn.Weight, weighIn.WeightUnit) + 6.25 * height - 5 * (day.Year - born) + (sex == Sex.Male ? 5 : -161);
        return (int)Math.Round(resting * Factor(settings.Activity));
    }

    /// <summary>The usual multipliers, from 1.2 for sitting most of the day to 1.9 for hard training on top of physical work.</summary>
    public static double Factor(ActivityLevel level) => level switch
    {
        ActivityLevel.Sedentary => 1.2,
        ActivityLevel.Light => 1.375,
        ActivityLevel.Moderate => 1.55,
        ActivityLevel.Very => 1.725,
        _ => 1.9,
    };

    /// <summary>
    /// Every day from <paramref name="from"/> through <paramref name="to"/>: what I logged eating, by the day on the clock when I
    /// ate it, and maintenance at the weigh-in nearest before the day was out (else the first one after).
    /// </summary>
    public static List<CalorieDay> Days(IEnumerable<FoodEntry> food, IEnumerable<BodyWeightEntry> weighIns, AppSettings settings, DateOnly from, DateOnly to)
    {
        var eaten = food.GroupBy(f => DateOnly.FromDateTime(f.At.DateTime)).ToDictionary(g => g.Key, g => g.Sum(f => f.Calories));
        var byTime = weighIns.OrderBy(w => w.At).ToList();
        var days = new List<CalorieDay>();
        var weighedBy = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            while (weighedBy < byTime.Count && DateOnly.FromDateTime(byTime[weighedBy].At.DateTime) <= day) weighedBy++;
            var weighIn = weighedBy > 0 ? byTime[weighedBy - 1] : byTime.FirstOrDefault();
            days.Add(new CalorieDay(day, eaten.TryGetValue(day, out var calories) ? calories : null, Maintenance(settings, weighIn, day)));
        }

        return days;
    }

    /// <summary>The days together: see <see cref="CalorieSpan"/>.</summary>
    public static CalorieSpan Span(IReadOnlyCollection<CalorieDay> days)
    {
        var logged = days.Where(d => d.Eaten is not null).ToList();
        var deficits = logged.Select(d => d.Deficit).OfType<int>().ToList();
        double? eaten = logged.Count == 0 ? null : logged.Average(d => d.Eaten!.Value);
        double? deficit = deficits.Count == 0 ? null : deficits.Average();
        return new CalorieSpan(days.Count, logged.Count, eaten, deficit);
    }
}
