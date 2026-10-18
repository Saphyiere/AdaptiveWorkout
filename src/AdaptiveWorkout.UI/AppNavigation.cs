using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace AdaptiveWorkout.UI;

/// <summary>
/// The way back, the same for a screen's back link and the phone's back gesture, which walks the web view's history. Opening a
/// screen is a step on. Going back, and finishing what a screen was opened for (saving, deleting, choosing), steps back to the
/// screen it was opened from, never on to a fresh copy of it. Switching views, or starting a workout from the session picker,
/// takes the screen's place. So the history is the way the screens were opened, each step back is one screen, and Today is never
/// far. Routes injects it, so it's made before the router and hears of every move before the next screen draws.
/// </summary>
public sealed class AppNavigation : IDisposable
{
    private readonly NavigationManager nav;
    private readonly IJSRuntime js;

    // The screens in the order they were opened; the last one is showing.
    private readonly List<Screen> screens;

    // Steps back asked of the web view that haven't arrived yet. Any other step back is the phone's back gesture: one screen.
    private int stepsBack;
    private bool opening;
    private bool replacing;

    public AppNavigation(NavigationManager nav, IJSRuntime js)
    {
        (this.nav, this.js) = (nav, js);
        screens = [new Screen(PathOf(nav.Uri))];
        nav.LocationChanged += OnLocationChanged;
    }

    /// <summary>What the screen before this one calls itself, for the way back; null when this one is the first.</summary>
    public string? PreviousName => screens.Count > 1 ? screens[^2].Name : null;

    /// <summary>Today is further back than the screen before, so a way straight there saves steps.</summary>
    public bool TodayIsFurtherBack => screens.SkipLast(2).Any(s => s.Path == "");

    /// <summary>What the screen showing calls itself, which the next screen's way back shows.</summary>
    public void Name(string name) => screens[^1].Name = name;

    /// <summary>Keeps something with the screen showing, like a form not saved yet, for when it's come back to.</summary>
    public void Keep(object state) => screens[^1].Kept = state;

    /// <summary>What the screen showing kept, now that it's been come back to; null when it's freshly opened.</summary>
    public T? Kept<T>() where T : class => screens[^1].Kept as T;

    /// <summary>Opens a screen, a step on from this one.</summary>
    public void Open(string path)
    {
        opening = true;
        nav.NavigateTo(path);
    }

    /// <summary>Shows another screen in this one's place, like the workout's other view.</summary>
    public void Replace(string path)
    {
        replacing = true;
        nav.NavigateTo(path, replace: true);
    }

    /// <summary>Back to the screen before; with none (the app opened on this one), <paramref name="otherwise"/> in this one's place.</summary>
    public Task BackAsync(string otherwise) => screens.Count > 1 ? GoBackAsync(1) : ReplaceAsync(otherwise);

    /// <summary>Back to the last <paramref name="path"/> before this screen, closing every screen since; with none, it takes this one's place.</summary>
    public Task ReturnToAsync(string path)
    {
        var at = screens.Count > 1 ? screens.FindLastIndex(screens.Count - 2, s => s.Path == path) : -1;
        return at >= 0 ? GoBackAsync(screens.Count - 1 - at) : ReplaceAsync(path);
    }

    /// <summary>Back to Today, closing every screen since.</summary>
    public Task HomeAsync() => ReturnToAsync("");

    public void Dispose() => nav.LocationChanged -= OnLocationChanged;

    private Task ReplaceAsync(string path)
    {
        Replace(path);
        return Task.CompletedTask;
    }

    private async Task GoBackAsync(int steps)
    {
        // A step back already on its way (Back tapped twice, say) is all that happens: the second would go one screen too far.
        if (stepsBack > 0) return;

        stepsBack = steps;
        try
        {
            await js.InvokeVoidAsync("history.go", -steps);
        }
        catch (JSDisconnectedException)
        {
            stepsBack = 0;
        }
    }

    // Blazor reports a step back like a tapped link, so they're told apart here: this knows the steps back it asked for, and any other
    // move to the screen before is the phone's back gesture. (No link leads to the screen before; going there is always a step back.)
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var path = PathOf(e.Location);
        if (replacing) screens[^1] = new Screen(path);
        else if (opening) screens.Add(new Screen(path));
        else if (stepsBack > 0)
        {
            SteppedBack(stepsBack, path);
            stepsBack = 0;
        }
        else if (screens.Count > 1 && screens[^2].Path == path) SteppedBack(1, path);
        else screens.Add(new Screen(path));
        (opening, replacing) = (false, false);
    }

    // Should a step back land elsewhere than expected, the location wins: back to that screen's last place in the history, or, if
    // it was never opened, a step on to it.
    private void SteppedBack(int steps, string path)
    {
        steps = Math.Min(steps, screens.Count - 1);
        screens.RemoveRange(screens.Count - steps, steps);
        if (screens[^1].Path == path) return;

        var at = screens.FindLastIndex(s => s.Path == path);
        if (at >= 0) screens.RemoveRange(at + 1, screens.Count - at - 1);
        else screens.Add(new Screen(path));
    }

    private string PathOf(string uri) => nav.ToBaseRelativePath(uri).Split('?', '#')[0];

    private sealed class Screen(string path)
    {
        public string Path { get; } = path;
        public string? Name { get; set; }
        public object? Kept { get; set; }
    }
}
