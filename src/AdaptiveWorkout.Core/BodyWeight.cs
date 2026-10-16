namespace AdaptiveWorkout.Core;

public static class BodyWeight
{
    /// <summary>The latest weigh-in minus the earliest one within the last <paramref name="days"/> days; null with fewer than two.</summary>
    public static double? Change(IEnumerable<BodyWeightEntry> entries, DateTimeOffset now, int days)
    {
        var recent = entries.Where(e => e.At >= now.AddDays(-days) && e.At <= now).OrderBy(e => e.At).ToList();
        return recent.Count < 2 ? null : recent[^1].Weight - recent[0].Weight;
    }
}
