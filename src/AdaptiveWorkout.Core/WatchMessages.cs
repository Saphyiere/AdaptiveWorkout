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

    /// <summary>Phone to watch: what the workout is doing now, as <see cref="EncodeStatus"/> writes it. Sent on every change.</summary>
    public const string Status = "/workout/status";

    /// <summary>Watch to phone: start whatever comes next, e.g. when the rest countdown is up.</summary>
    public const string Next = "/workout/next";

    /// <summary>Watch to phone: the watch app just started reading, so send the status again.</summary>
    public const string Hello = "/workout/hello";

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

    /// <summary>One line each: rest or set, Since and RestEndsAt in Unix milliseconds (0 when empty), Title, Next (empty when none).</summary>
    public static byte[] EncodeStatus(WorkoutStatus status) => Encoding.UTF8.GetBytes(string.Join('\n',
        status.Resting ? "rest" : "set",
        status.Since.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
        (status.RestEndsAt?.ToUnixTimeMilliseconds() ?? 0).ToString(CultureInfo.InvariantCulture),
        status.Title,
        status.Next ?? ""));

    public static WorkoutStatus DecodeStatus(byte[] payload)
    {
        var lines = Encoding.UTF8.GetString(payload).Split('\n');
        var restEndsAt = long.Parse(lines[2], CultureInfo.InvariantCulture);
        return new WorkoutStatus(
            lines[0] == "rest",
            lines[3],
            DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(lines[1], CultureInfo.InvariantCulture)).ToLocalTime(),
            restEndsAt == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(restEndsAt).ToLocalTime(),
            lines[4].Length == 0 ? null : lines[4]);
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
