namespace AdaptiveWorkout.Core;

/// <summary>
/// One day's food: the calories I logged eating (empty on a day I logged none), and maintenance that day (empty until it can be worked
/// out), which includes what that day's workouts burned (<paramref name="Workouts"/>).
/// </summary>
public sealed record CalorieDay(DateOnly Day, int? Eaten, int? Maintenance, int Workouts = 0)
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

/// <summary>
/// Am I eating to my goal, and what should it do to my weight? What I ate is what I log. Maintenance comes from my body and how I spend
/// a usual day, and each workout adds what it burned on the day I did it.
/// </summary>
public static class Calories
{
    /// <summary>About what a pound of body weight is worth, in calories.</summary>
    public const double PerPound = 3500;

    /// <summary>About what a kilogram of body weight is worth, in calories.</summary>
    public const double PerKilogram = 7700;

    /// <summary>How far off a workout's burn could be, either way: estimates like it are within about a third at best.</summary>
    public const double BurnedError = 0.35;

    // How hard a workout was, as METs (a multiple of resting) at a share of my maximum heart rate. A usual lifting workout is weight
    // training (3.5) and a hard one vigorous lifting (6), from the 2024 Adult Compendium of Physical Activities; the heart rate takes
    // it from one to the other between ACSM's moderate and vigorous bands (64% and 77% of maximum). A workout of only timed
    // exercises, like a walk or a run, goes on to ACSM's near-maximal (8.8 at 96%).
    private static readonly (double Share, double Mets) Usual = (0.64, 3.5), Vigorous = (0.77, 6), NearMaximal = (0.96, 8.8);

    /// <summary>
    /// Calories a day that would keep my weight where it is on a day without a workout: resting calories by the Mifflin-St Jeor
    /// formula (10 × kg + 6.25 × cm − 5 × age, then +5 for a man or −161 for a woman), times how much I move on a usual day.
    /// Empty without my sex, birth year, height or a weigh-in.
    /// </summary>
    public static int? Maintenance(AppSettings settings, BodyWeightEntry? weighIn, DateOnly day)
    {
        if (settings is not { Sex: { } sex, BirthYear: int born, HeightCm: double height } || weighIn is null) return null;

        var resting = 10 * WorkoutStats.Kilograms(weighIn.Weight, weighIn.WeightUnit) + 6.25 * height - 5 * (day.Year - born) + (sex == Sex.Male ? 5 : -161);
        return (int)Math.Round(resting * Factor(settings.DailyActivity));
    }

    /// <summary>The usual multipliers for a day without exercise, from 1.2 sitting most of it to 1.725 for physical work all day.</summary>
    public static double Factor(DailyActivity level) => level switch
    {
        DailyActivity.Sitting => 1.2,
        DailyActivity.PartlyOnFeet => 1.375,
        DailyActivity.MostlyOnFeet => 1.55,
        _ => 1.725,
    };

    /// <summary>
    /// When a workout was on: from Start to its last set, or to Finish when it ended with a cool-down (or had no sets). So a workout
    /// left open after its last set doesn't count the time until Finish was tapped.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) WorkoutTime(WorkoutSession session, IEnumerable<WorkoutSet> sets)
    {
        var finished = session.FinishedAt ?? session.StartedAt;
        var lastStop = sets.Max(s => s.StoppedAt);
        return (session.StartedAt, session.CoolDownStartedAt is null && lastStop < finished ? lastStop.Value : finished);
    }

    /// <summary>
    /// Roughly what a workout burned on top of resting, in calories: its time × (METs − 1) × kg. Lifting is 3.5 METs, and more as the
    /// average heart rate climbs from 64% to 77% of my maximum (208 − 0.7 × age), up to 6; a workout of only timed exercises goes on
    /// up to 8.8. Without readings or my age, it's 3.5. Empty without a weigh-in. Within <see cref="BurnedError"/> at best.
    /// </summary>
    public static int? Burned(TimeSpan time, double? averageBpm, bool lifting, AppSettings settings, BodyWeightEntry? weighIn, DateOnly day)
    {
        if (weighIn is null) return null;

        double Between(double share, (double Share, double Mets) low, (double Share, double Mets) high) =>
            low.Mets + (Math.Clamp(share, low.Share, high.Share) - low.Share) / (high.Share - low.Share) * (high.Mets - low.Mets);
        var mets = Usual.Mets;
        if (averageBpm is double bpm and > 0 && settings.BirthYear is int born)
        {
            var share = bpm / (208 - 0.7 * (day.Year - born));
            mets = share <= Vigorous.Share || lifting ? Between(share, Usual, Vigorous) : Between(share, Vigorous, NearMaximal);
        }

        return (int)Math.Round((mets - 1) * WorkoutStats.Kilograms(weighIn.Weight, weighIn.WeightUnit) * Math.Max(0, time.TotalHours));
    }

    /// <summary>What each day's finished workouts burned on top of resting (see <see cref="Burned"/>), by the day each started.</summary>
    public static Dictionary<DateOnly, int> BurnedByDay(IEnumerable<WorkoutSession> sessions, IEnumerable<WorkoutSet> sets,
        IReadOnlyDictionary<long, Exercise> exercises, IEnumerable<BodyWeightEntry> weighIns, AppSettings settings)
    {
        var setsBySession = sets.ToLookup(s => s.SessionId);
        var inOrder = BodyWeight.InOrder(weighIns);
        var burned = new Dictionary<DateOnly, int>();
        foreach (var session in sessions.Where(s => s.FinishedAt is not null))
        {
            var theirs = setsBySession[session.Id].ToList();
            var (from, to) = WorkoutTime(session, theirs);
            var lifting = theirs.Any(s => exercises.GetValueOrDefault(s.ExerciseId)?.Measure == ExerciseMeasure.Weight);
            var day = DateOnly.FromDateTime(session.StartedAt.DateTime);
            var calories = Burned(to - from, session.AverageBpm, lifting, settings, BodyWeight.Nearest(inOrder, session.StartedAt), day);
            if (calories is int some) burned[day] = burned.GetValueOrDefault(day) + some;
        }

        return burned;
    }

    /// <summary>
    /// Every day from <paramref name="from"/> through <paramref name="to"/>: what I logged eating, by the day on the clock when I
    /// ate it, and maintenance at the weigh-in nearest before the day was out (else the first one after), with what that day's
    /// workouts burned (<paramref name="burned"/>, from <see cref="BurnedByDay"/>).
    /// </summary>
    public static List<CalorieDay> Days(IEnumerable<FoodEntry> food, IEnumerable<BodyWeightEntry> weighIns, AppSettings settings, DateOnly from, DateOnly to,
        IReadOnlyDictionary<DateOnly, int> burned)
    {
        var eaten = food.GroupBy(f => DateOnly.FromDateTime(f.At.DateTime)).ToDictionary(g => g.Key, g => g.Sum(f => f.Calories));
        var byTime = weighIns.OrderBy(w => w.At).ToList();
        var days = new List<CalorieDay>();
        var weighedBy = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            while (weighedBy < byTime.Count && DateOnly.FromDateTime(byTime[weighedBy].At.DateTime) <= day) weighedBy++;
            var weighIn = weighedBy > 0 ? byTime[weighedBy - 1] : byTime.FirstOrDefault();
            var workouts = burned.GetValueOrDefault(day);
            days.Add(new CalorieDay(day, eaten.TryGetValue(day, out var calories) ? calories : null, Maintenance(settings, weighIn, day) + workouts, workouts));
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
