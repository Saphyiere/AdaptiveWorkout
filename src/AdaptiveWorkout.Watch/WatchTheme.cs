using Android.Content;
using Android.Graphics;
using Android.OS;

namespace AdaptiveWorkout.Watch;

/// <summary>A font and its variation axes as the phone's app.css sets them, and whether its words go in capitals, like the phone's labels.</summary>
public sealed record WatchFont(int Font, string Axes, bool Caps = false);

/// <summary>
/// The phone's theme on the watch: its colors and fonts, from the phone's app.css tokens. The phone says which theme it wears;
/// the watch keeps the last one it heard. Dimmed, every theme goes to light print on black, to spare the screen.
/// </summary>
/// <param name="Heart">The heart rate's color: ink in a logbook, the heart channel's coral on an instrument panel.</param>
/// <param name="Mark">What's live or over: the logbook's red pen, the panel's lime.</param>
/// <param name="Slab">The big button's fill, with <paramref name="OnSlab"/> for its words.</param>
/// <param name="Labels">Small words: the time, and what the workout is doing.</param>
/// <param name="Unit">"bpm", and notes like "Not reaching the phone".</param>
public sealed record WatchTheme(string Id, Color Ground, Color Ink, Color Muted, Color Heart, Color Mark, Color Slab, Color OnSlab, int SlabRadius,
    WatchFont Numbers, WatchFont Labels, WatchFont Unit, WatchFont Control)
{
    private const string Preferences = "watch";
    private const string Key = "theme";

    public static readonly WatchTheme Logbook = new("logbook", Color.ParseColor("#F3EFE6"), Color.ParseColor("#1A1814"), Color.ParseColor("#5E574B"),
        Color.ParseColor("#1A1814"), Color.ParseColor("#AE2F18"), Color.ParseColor("#1A1814"), Color.ParseColor("#F3EFE6"), 2,
        new(Resource.Font.newsreader, "'wght' 600"), new(Resource.Font.schibsted_grotesk, "'wght' 600", Caps: true), new(Resource.Font.newsreader_italic, ""),
        new(Resource.Font.schibsted_grotesk, "'wght' 700"));

    public static readonly WatchTheme Telemetry = new("telemetry", Color.ParseColor("#0B0F14"), Color.ParseColor("#E8EEF2"), Color.ParseColor("#95A2AF"),
        Color.ParseColor("#FF6A5C"), Color.ParseColor("#B8F04A"), Color.ParseColor("#B8F04A"), Color.ParseColor("#0B0F14"), 8,
        new(Resource.Font.martian_mono, "'wdth' 75, 'wght' 600"), new(Resource.Font.martian_mono, "'wdth' 100, 'wght' 500", Caps: true),
        new(Resource.Font.martian_mono, "'wdth' 100, 'wght' 500", Caps: true), new(Resource.Font.atkinson_hyperlegible_next, "'wght' 700"));

    /// <summary>Print for the dimmed screen: light on black, whatever the theme.</summary>
    public static readonly Color DimInk = Color.ParseColor("#E8E8E8");

    public static readonly Color DimMuted = Color.ParseColor("#8C8C8C");

    /// <summary>Raised on the main thread when the phone changes its theme.</summary>
    public static event Action? Changed;

    /// <summary>The theme the phone wore when it last said; Logbook, like the phone, before it has.</summary>
    public static WatchTheme Current(Context context) =>
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)!.GetString(Key, null) == Telemetry.Id ? Telemetry : Logbook;

    /// <summary>Keeps the phone's theme, by its id, for now and for next time. Any thread.</summary>
    public static void Save(Context context, string id)
    {
        context.GetSharedPreferences(Preferences, FileCreationMode.Private)!.Edit()!.PutString(Key, id)!.Apply();
        new Handler(Looper.MainLooper!).Post(() => Changed?.Invoke());
    }
}
