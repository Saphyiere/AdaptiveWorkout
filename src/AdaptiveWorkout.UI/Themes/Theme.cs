namespace AdaptiveWorkout.UI.Themes;

/// <summary>
/// A look the app can wear. app.css holds its colors, fonts and shapes under [data-theme="<see cref="Id"/>"]; the few
/// components that draw differently per theme (a pen tick, or a ruler under a number) get the theme in use as a cascading value.
/// </summary>
/// <param name="Dark">Light text on a dark page, so the host's status bar icons go light too.</param>
/// <param name="Ground">The page's background (app.css's --ground), for the host to show behind the page while it loads.</param>
/// <param name="TodayFormat">Today's date over its title: "Sunday, October 18", or "Sun 18 Oct".</param>
/// <param name="DayFormat">A past workout's date in a row: "Fri, Oct 9", or "Fri 09 Oct".</param>
public sealed record Theme(string Id, string Name, string Description, bool Dark, string Ground, string TodayFormat, string DayFormat)
{
    public static readonly Theme Logbook = new("logbook", "Logbook", "A printed training log: warm paper, serif print, and a red pen that marks what you did.",
        Dark: false, Ground: "#F3EFE6", TodayFormat: "dddd, MMMM d", DayFormat: "ddd, MMM d");

    public static readonly Theme Telemetry = new("telemetry", "Telemetry", "An instrument panel: dark, with mono readouts and every number on a ruler.",
        Dark: true, Ground: "#0B0F14", TodayFormat: "ddd d MMM", DayFormat: "ddd dd MMM");

    public static IReadOnlyList<Theme> All { get; } = [Logbook, Telemetry];

    /// <summary>The theme with this id, or Logbook when there's none (or it's gone).</summary>
    public static Theme FromId(string? id) => All.FirstOrDefault(t => t.Id == id) ?? Logbook;
}
