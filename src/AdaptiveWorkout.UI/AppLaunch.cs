using System.Diagnostics;

namespace AdaptiveWorkout.UI;

/// <summary>
/// Times the app's opening, up to Today drawn with its data, and writes it once as one line: the whole time, then how long it took
/// to reach each step, so a slow start shows where its time went. The host adds steps of its own (its code starting, its window,
/// the web view) and decides where the line goes.
/// </summary>
public class AppLaunch
{
    private readonly Lock gate = new();
    private readonly long startedAt;
    private readonly List<(string Name, long At)> steps = [];
    private bool written;

    public AppLaunch() : this(TimeSpan.Zero)
    {
    }

    /// <summary>For a host whose opening began before this was created, e.g. when its process started.</summary>
    protected AppLaunch(TimeSpan alreadyRunning) => startedAt = Stopwatch.GetTimestamp() - (long)(alreadyRunning.TotalSeconds * Stopwatch.Frequency);

    /// <summary>
    /// The opening reached a step: now, or at a <see cref="Stopwatch"/> timestamp taken earlier. Reached again before Today is drawn
    /// (a page drawn ahead of time by a web host, say), the later time counts.
    /// </summary>
    public void Step(string name, long? at = null)
    {
        lock (gate)
        {
            if (written) return;

            steps.RemoveAll(s => s.Name == name);
            steps.Add((name, at ?? Stopwatch.GetTimestamp()));
        }
    }

    /// <summary>Today is drawn with its data, which ends the opening. Writes the line, the first time only.</summary>
    public void Drawn()
    {
        string line;
        lock (gate)
        {
            if (written) return;

            written = true;
            steps.Add(("drawn", Stopwatch.GetTimestamp()));
            var reached = startedAt;
            var parts = new List<string>();
            foreach (var (name, at) in steps.OrderBy(s => s.At))
            {
                parts.Add($"{name} +{Stopwatch.GetElapsedTime(reached, at).TotalMilliseconds:0}");
                reached = at;
            }
            line = $"Opened in {Stopwatch.GetElapsedTime(startedAt, reached).TotalMilliseconds:0} ms: {string.Join(", ", parts)}";
        }

        Write(line);
    }

    /// <summary>Where the line goes: the console, unless the host has a better place for it. Called once, on any thread.</summary>
    protected virtual void Write(string line) => Console.WriteLine($"AdaptiveWorkout: {line}");
}
