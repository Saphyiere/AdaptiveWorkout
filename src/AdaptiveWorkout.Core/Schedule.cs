using System.Globalization;

namespace AdaptiveWorkout.Core;

/// <summary>A routine's week: each day's session, Monday first, with null for rest days.</summary>
public static class Schedule
{
    /// <summary>Monday is 0, Sunday is 6.</summary>
    public static int DayIndex(DateOnly day) => ((int)day.DayOfWeek + 6) % 7;

    /// <summary>"Mon" for 0 through "Sun" for 6, in the phone's language.</summary>
    public static string ShortName(int index) => CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(index + 1) % 7];

    /// <summary>"Monday" for 0 through "Sunday" for 6, in the phone's language.</summary>
    public static string Name(int index) => CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(index + 1) % 7];

    /// <summary>The session planned for this day, or null on a rest day.</summary>
    public static long? On(IReadOnlyList<long?> week, DateOnly day) => week[DayIndex(day)];
}
