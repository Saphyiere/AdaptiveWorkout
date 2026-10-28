namespace AdaptiveWorkout.Core;

public static class BodyWeight
{
    private static readonly Comparer<BodyWeightEntry> ByTime = Comparer<BodyWeightEntry>.Create((a, b) => a.At.CompareTo(b.At));

    /// <summary>The weigh-ins in time order, for <see cref="Nearest"/>.</summary>
    public static List<BodyWeightEntry> InOrder(IEnumerable<BodyWeightEntry> entries) => [.. entries.Order(ByTime)];

    /// <summary>
    /// What I weighed at a time: the weigh-in nearest before it, else the first one after; empty without any. The weigh-ins are in
    /// time order (<see cref="InOrder"/>), so it finds its own without reading them all.
    /// </summary>
    public static BodyWeightEntry? Nearest(List<BodyWeightEntry> inOrder, DateTimeOffset at)
    {
        var index = inOrder.BinarySearch(new BodyWeightEntry { At = at }, ByTime);
        return index >= 0 ? inOrder[index] : ~index > 0 ? inOrder[~index - 1] : inOrder.FirstOrDefault();
    }

    /// <summary>The latest weigh-in minus the earliest one within the last <paramref name="days"/> days; null with fewer than two.</summary>
    public static double? Change(IEnumerable<BodyWeightEntry> entries, DateTimeOffset now, int days)
    {
        var recent = entries.Where(e => e.At >= now.AddDays(-days) && e.At <= now).OrderBy(e => e.At).ToList();
        return recent.Count < 2 ? null : recent[^1].Weight - recent[0].Weight;
    }

    /// <summary>
    /// How far my weight went up or down from <paramref name="from"/> to <paramref name="to"/>, in <paramref name="unit"/>, read off the
    /// straight line through the weigh-ins in that time, so one heavy morning doesn't swing it. Empty without two weigh-ins a week apart.
    /// </summary>
    public static double? Trend(IEnumerable<BodyWeightEntry> entries, DateTimeOffset from, DateTimeOffset to, WeightUnit unit)
    {
        var inTime = entries.Where(e => e.At >= from && e.At <= to);
        var points = inTime.Select(e => (Day: (e.At - from).TotalDays, Pounds: WorkoutStats.Pounds(e.Weight, e.WeightUnit))).ToList();
        if (points.Count < 2 || points.Max(p => p.Day) - points.Min(p => p.Day) < 7) return null;

        var (meanDay, meanPounds) = (points.Average(p => p.Day), points.Average(p => p.Pounds));
        var slope = points.Sum(p => (p.Day - meanDay) * (p.Pounds - meanPounds)) / points.Sum(p => (p.Day - meanDay) * (p.Day - meanDay));
        var pounds = slope * (to - from).TotalDays;
        return unit == WeightUnit.Kilograms ? WorkoutStats.Kilograms(pounds, WeightUnit.Pounds) : pounds;
    }
}
