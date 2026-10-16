namespace AdaptiveWorkout.Core;

/// <summary>
/// Classic five-zone model: zone 1 starts at 50% of max heart rate and each zone is a 10% band.
/// Placeholder rules to show where business logic and its tests go; replace once the real workout rules are decided.
/// </summary>
public static class HeartRateZones
{
    /// <summary>Returns 0 when below zone 1, otherwise 1 through 5. Readings above max heart rate stay in zone 5.</summary>
    public static int ZoneFor(int bpm, int maxHeartRate)
    {
        if (maxHeartRate <= 0) throw new ArgumentOutOfRangeException(nameof(maxHeartRate), maxHeartRate, "Max heart rate must be positive.");

        // Whole percent, rounded down, so a reading only enters a zone once it actually reaches the threshold.
        var percentOfMax = bpm * 100 / maxHeartRate;
        return Math.Clamp((percentOfMax - 40) / 10, 0, 5);
    }
}
