using System.Globalization;
using System.Text;
using AdaptiveWorkout.Core;
using AdaptiveWorkout.UI.Storage;
using AdaptiveWorkout.UI.Themes;
using AdaptiveWorkout.UI.Watch;
using Android.Content;
using Android.Gms.Extensions;
using Android.Gms.Wearable;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Application = Android.App.Application;

namespace AdaptiveWorkout.Phone;

/// <summary>Reaches the watch app over the Wear OS Data Layer. The watch wears the phone's theme, so it hears of every change.</summary>
public sealed class PhoneWatchLink : WatchLink
{
    // What androidx.wear.remote.interactions.RemoteActivityHelper sends, which has no .NET binding: the watch's companion app
    // on the phone (Galaxy Wearable's plugin for a Galaxy Watch) passes the intent to the watch, and the watch opens it.
    private const string RemoteIntent = "com.google.android.wearable.intent.action.REMOTE_INTENT";
    private const string DefaultCompanion = "com.google.android.wearable.app";

    private readonly ThemeSetting themes;

    public PhoneWatchLink(WorkoutStore store, ThemeSetting themes) : base(store)
    {
        this.themes = themes;
        themes.Changed += ShowTheme;
    }

    public override void Open(WorkoutSession session) => _ = ToEachWatchAsync(async (context, watch) =>
    {
        var companion = (await WearableClass.GetNodeClient(context).GetCompanionPackageForNode(watch.Id))?.ToString();
        var screen = new Intent(Intent.ActionView, Android.Net.Uri.Parse(WatchMessages.WorkoutUri(session.StartedAt))).AddCategory(Intent.CategoryBrowsable);

        // Like RemoteActivityHelper, passes the receiver as a plain ResultReceiver so the companion app needs none of this app's classes.
        var parcel = Parcel.Obtain();
        new OpenResult().WriteToParcel(parcel, ParcelableWriteFlags.None);
        parcel.SetDataPosition(0);
        var result = ResultReceiver.Creator!.CreateFromParcel(parcel)!.JavaCast<ResultReceiver>()!;
        parcel.Recycle();

        var request = new Intent(RemoteIntent)
            .PutExtra("com.google.android.wearable.intent.extra.INTENT", screen)
            .PutExtra("com.google.android.wearable.intent.extra.NODE_ID", watch.Id)
            .PutExtra("com.google.android.wearable.intent.extra.RESULT_RECEIVER", result)
            .SetPackage(string.IsNullOrEmpty(companion) ? DefaultCompanion : companion);
        context.SendBroadcast(request);

        // Backup: the watch app shows a "workout started" notification if its screen didn't open.
        var startedAt = Encoding.UTF8.GetBytes(session.StartedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        await WearableClass.GetMessageClient(context).SendMessageAsync(watch.Id, WatchMessages.Theme, Encoding.UTF8.GetBytes(themes.Current.Id));
        await WearableClass.GetMessageClient(context).SendMessageAsync(watch.Id, WatchMessages.Start, startedAt);
    });

    /// <summary>Tells the watch which theme the phone wears, so its screen wears it too.</summary>
    public void ShowTheme() =>
        _ = ToEachWatchAsync((context, watch) => WearableClass.GetMessageClient(context).SendMessageAsync(watch.Id, WatchMessages.Theme, Encoding.UTF8.GetBytes(themes.Current.Id)));

    public override void Close() => _ = ToEachWatchAsync((context, watch) => WearableClass.GetMessageClient(context).SendMessageAsync(watch.Id, WatchMessages.Stop, []));

    public override void Show(WorkoutStatus status) =>
        _ = ToEachWatchAsync((context, watch) => WearableClass.GetMessageClient(context).SendMessageAsync(watch.Id, WatchMessages.Status, WatchMessages.EncodeStatus(status)));

    private static async Task ToEachWatchAsync(Func<Context, INode, Task> send)
    {
        try
        {
            var context = Application.Context;
            foreach (var watch in (await WearableClass.GetNodeClient(context).GetConnectedNodesAsync()).OfType<INode>()) await send(context, watch);
        }
        catch (Exception ex)
        {
            Log.Warn("AdaptiveWorkout", $"Reaching the watch failed: {ex.Message}");
        }
    }

    // The companion app's answer, for adb logcat: 0 means the watch opened the screen.
    private sealed class OpenResult() : ResultReceiver(null)
    {
        protected override void OnReceiveResult(int resultCode, Bundle? resultData) => Log.Info("AdaptiveWorkout", $"Opening the watch app: {(resultCode == 0 ? "done" : $"failed ({resultCode})")}");
    }
}
