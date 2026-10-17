namespace AdaptiveWorkout.Core;

/// <summary>Numbers for looking back at a finished workout.</summary>
public static class WorkoutStats
{
    private const double PoundsPerKilogram = 2.20462;

    /// <summary>Total weight moved: weight × reps over every weighted set, in <paramref name="unit"/>. Bodyweight and assisted sets add nothing.</summary>
    public static double Volume(IEnumerable<WorkoutSet> sets, WeightUnit unit) => sets.Where(s => s.Weight > 0 && s.Reps > 0).Sum(s =>
    {
        var weight = s.WeightUnit == unit ? s.Weight!.Value : unit == WeightUnit.Pounds ? s.Weight!.Value * PoundsPerKilogram : s.Weight!.Value / PoundsPerKilogram;
        return weight * s.Reps!.Value;
    });

    /// <summary>Average and highest heart rate from <paramref name="from"/> to <paramref name="to"/>; null without readings.</summary>
    public static (int Average, int Max)? HeartRate(IEnumerable<HeartRateSample> samples, DateTimeOffset from, DateTimeOffset to)
    {
        var bpms = samples.Where(s => s.At >= from && s.At <= to).Select(s => s.Bpm).ToList();
        return bpms.Count == 0 ? null : ((int)Math.Round(bpms.Average()), bpms.Max());
    }
}
