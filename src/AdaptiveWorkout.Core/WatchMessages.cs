using System.Globalization;
using System.Text;

namespace AdaptiveWorkout.Core;

/// <summary>What the phone and watch apps send each other over the Wear OS Data Layer.</summary>
public static class WatchMessages
{
    /// <summary>Phone to watch: a workout started; the payload is its start time in Unix milliseconds. The phone also opens <see cref="WorkoutUri"/> on the watch.</summary>
    public const string Start = "/workout/start";

    /// <summary>Phone to watch: the workout is over, so stop reading heart rate.</summary>
    public const string Stop = "/workout/stop";

    /// <summary>Watch to phone: heart rate readings, as <see cref="EncodeReadings"/> writes them.</summary>
    public const string HeartRate = "/heart-rate";

    /// <summary>Opens the watch app's heart rate screen for a workout.</summary>
    public static string WorkoutUri(DateTimeOffset startedAt) => $"adaptiveworkout://workout?started={startedAt.ToUnixTimeMilliseconds()}";

    /// <summary>A line with the phone workout's start (0 when the watch app was opened on its own), then a line per reading: Unix milliseconds and bpm.</summary>
    public static byte[] EncodeReadings(DateTimeOffset? workoutStartedAt, IEnumerable<HeartRateSample> readings)
    {
        var lines = readings.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.At.ToUnixTimeMilliseconds()} {r.Bpm}"));
        return Encoding.UTF8.GetBytes(string.Join('\n', lines.Prepend((workoutStartedAt?.ToUnixTimeMilliseconds() ?? 0).ToString(CultureInfo.InvariantCulture))));
    }

    public static (DateTimeOffset? WorkoutStartedAt, List<HeartRateSample> Readings) DecodeReadings(byte[] payload)
    {
        var lines = Encoding.UTF8.GetString(payload).Split('\n');
        var startedAt = long.Parse(lines[0], CultureInfo.InvariantCulture);
        var readings = lines.Skip(1).Select(line => line.Split(' ')).Select(parts => new HeartRateSample
        {
            At = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(parts[0], CultureInfo.InvariantCulture)).ToLocalTime(),
            Bpm = int.Parse(parts[1], CultureInfo.InvariantCulture),
        });
        return (startedAt == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(startedAt).ToLocalTime(), [.. readings]);
    }
}
