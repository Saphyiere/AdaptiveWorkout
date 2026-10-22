# AdaptiveWorkout

Personal workout app for an Android phone, with heart rate coming from a Samsung Galaxy Watch5 (Wear OS; Wear OS 6 once updated to One UI 8 Watch). Not meant for app stores.

## Layout

```
build/
  AdaptiveWorkout.Build/    Solution items project (compiles nothing). Shows every repo-root file
                            (README, Directory.Build.props, global.json, ...) in Solution Explorer.
                            Put local-dev files here.
docs/                       The app concept and data model (concept.md).
src/
  AdaptiveWorkout.Core/     Business logic (plain .NET 10): next step, plan changes, suggestions, history, schedule, consistency, improvement.
  AdaptiveWorkout.UI/       The app's screens (Razor) and storage (SQLite via Dapper). Any host can show them.
  AdaptiveWorkout.Phone/    Phone app: .NET MAUI Blazor Hybrid host for the UI, Android only.
  AdaptiveWorkout.Watch/    Watch app: .NET for Android targeting Wear OS. Shows heart rate and sends it to the phone.
tests/
  AdaptiveWorkout.Core.Tests/   xUnit v3 tests for Core. Business logic only.
artifacts/                  All bin/obj output (git-ignored, set in Directory.Build.props).
```

Open `AdaptiveWorkout.slnx` in Visual Studio 2026 or Rider.

What the app does and how its data is shaped: [docs/concept.md](docs/concept.md).

## One-time setup (Windows)

1. Install **Visual Studio 2026** with the **.NET Multi-platform App UI development** workload. That brings the .NET 10 SDK, the Android SDK, and a JDK.
   - CLI-only alternative: install the .NET 10 SDK, then `dotnet workload install maui-android`, then
     `dotnet build src/AdaptiveWorkout.Phone -t:InstallAndroidDependencies -p:AndroidSdkDirectory=C:\Android\sdk -p:JavaSdkDirectory=C:\Android\jdk -p:AcceptAndroidSDKLicenses=True`
     (downloads the Android SDK and a JDK into those folders).
2. `adb` lives in the Android SDK's `platform-tools` folder (Visual Studio: `C:\Program Files (x86)\Android\android-sdk\platform-tools`, or **Tools > Android > Android Adb Command Prompt**).

## Hello world on the phone

1. Phone (Pixel): **Settings > About phone**, tap **Build number** 7 times. Newer Pixels show a short Developer options menu even before this, but **USB debugging** and **Wireless debugging** only appear after tapping Build number. Then **Settings > System > Developer options**, turn on **USB debugging** (or **Wireless debugging** if you'd rather not plug in).
2. Plug the phone in and accept the "Allow USB debugging" prompt. `adb devices` should list it.
3. Visual Studio: set **AdaptiveWorkout.Phone** as the startup project, pick the phone in the device dropdown, and press F5.
   CLI: `dotnet build src/AdaptiveWorkout.Phone -t:Run`

The app opens on Today. F5 runs a Debug build, which opens slowly: it compiles each method on the phone the first time it runs, and with the debugger attached it also waits for Visual Studio. (`UseInterpreter` is off in both apps, so Debug builds at least run compiled code rather than Mono's interpreter, which gives up C# Hot Reload on the devices.) **For the copy you use day to day, install Release**: pick **Release** in Visual Studio's configuration dropdown and press **Ctrl+F5**, or run `dotnet build src/AdaptiveWorkout.Phone -t:Run -c Release`. Release compiles the whole app ahead of time (`AndroidEnableProfiledAot` is off), so it opens quickly. It takes a few minutes to build, installs over a Debug build, and keeps the app's data.

Each start writes how long it took to the phone's log, as one line that `adb logcat -s AdaptiveWorkout` shows, like this (numbers made up):

```
Opened in 1200 ms: .NET +300, app +50, window +100, web view +200, Blazor +350, settings +50, Today's data +50, drawn +100
```

Each step's number is the time since the step before it, counting from when Android started the app: `.NET` until the app's own code runs, `app` building the MAUI app, `window` its window and page, `web view` creating the web view, `Blazor` loading the page and starting Blazor in it, `settings` opening the database and reading the theme, `Today's data` Today's reads, and `drawn` Today rendered with them. Android times the start too, from the tap: `adb logcat -s ActivityTaskManager` shows `Fully drawn …: +1s200ms`.

If the app stops as it opens with *No view found for id 0x… for fragment NavigationRootManager_ElementBasedFragment*, the build deployed resource ids that don't match the app's resources. `Directory.Build.targets` stops Visual Studio's background builds from causing that; if it happens anyway, delete the `artifacts` folder and build again. Don't uninstall the app to fix it: that deletes its data.

## Hello world on the watch (no USB needed)

Galaxy Watch4 and newer run Wear OS, so they install apps over Wi-Fi with `adb`.

1. Watch: **Settings > About watch > Software information**, tap **Software version** until developer mode turns on.
2. **Settings > Developer options**: turn on **ADB debugging** and **Wireless debugging**.
3. Put the watch on the same Wi-Fi network as the PC. Wear OS turns the watch's Wi-Fi off whenever it has a Bluetooth link to the phone (the Watch4 and newer have no "always on" Wi-Fi option), so while deploying, turn Bluetooth off on the phone. **Settings > Connections > Wi-Fi** on the watch should then show the network as connected.
4. **Wireless debugging > Pair new device** shows an IP, a pairing port, and a code:
   ```
   adb pair 192.168.1.50:37000      # pairing port, then enter the code
   adb connect 192.168.1.50:41234   # the *other* port shown on the Wireless debugging screen
   adb devices
   ```
5. Visual Studio: set **AdaptiveWorkout.Watch** as the startup project, pick the watch, press F5.
   CLI (with the phone also connected, name the watch): `dotnet build src/AdaptiveWorkout.Watch -t:Run -p:AdbTarget="-s 192.168.1.50:41234"`

Pairing is a one-time step. The connect port usually changes when the watch reboots or Wireless debugging is toggled, so check the screen and repeat `adb connect` when that happens.

## Heart rate: first run

The phone opens the watch app when a workout starts, and the watch sends heart rate back over the Wear OS Data Layer.

1. Deploy both apps from the same PC. The Data Layer only connects apps with the same id and signing key, and Debug and Release builds on one PC both use its debug key. If you ever set up a release keystore, sign both apps with it.
2. Turn Bluetooth back on on the phone after deploying to the watch: the watch and phone talk over that link.
3. Open **AdaptiveWorkout** on the watch once by hand and allow heart rate and notifications. It shows your heart rate right away. Swipe it away when done.
4. Start a workout on the phone (allow its notifications, for the rest timer on the lock screen). The watch wakes up on the heart rate screen, and within a few seconds the phone shows the bpm under the timer. **Finish** closes the watch app.
5. On a break, tap the rest time for a countdown. At zero the watch buzzes and the next set starts, even with the phone locked in a pocket: the watch keeps the time and tells the phone. The watch comes up showing the set; while a set runs, its button is **Stop**, and between sets **Start <next>**.
6. The watch screen stays up for the whole workout, dimmed between glances, instead of going back to the watch face: the workout is an ongoing activity, and its icon on the watch face taps back in.

If the watch doesn't open, it should at least show a "Workout started" notification; tap it. Then check the phone's log with `adb logcat -s AdaptiveWorkout`: `Opening the watch app: done` or `failed (n)` says what the watch's companion app answered. **Open on watch** on the phone tries again.

If an Android build ever fails over output paths, temporarily remove the two artifacts lines in `Directory.Build.props` to rule out the `artifacts/` output folder.

## Design

The phone app has two looks, switched in **Settings** (the icon at the top right of Today). The switch applies at once and is remembered.

- **Logbook:** a printed training log on warm paper, kept like a journal. Serif words and big serif numbers in fixed-width figures, no cards or boxes: each section is set apart by space and a heading the pen underlines once, rows sit on hairlines, and a faint tint inside a field says it's a place to write. The one vermilion pen also marks what you did: a tick on each gym day, a circle around today, tally strokes for the streak and the sets done, a tick over Hit goal, and a bracket down a superset. The pen marks; it never grades a number.
- **Telemetry:** the body as live instrument channels on a dark panel. Monospace readouts that never jitter (Martian Mono, slashed zero), words in Atkinson Hyperlegible Next, tiles, and every number on its own ruler in its channel's color: lime for training, amber for food, blue for body weight, coral for heart rate. The set's time fills a minute of seconds, the rest drains its countdown, calories sit on a ruler with the goal and maintenance marked, and the heart rate shows its last minute as a line.

Design links:

- Style directions, with both looks' mockups: https://claude.ai/artifact/2Z6jpTXARxNPY9AWAS2hxQ
- The first design system (the mark and app icon): https://claude.ai/artifact/H9wJAoBQE834PEQ9qG4wmq

**How themes work.** `Themes/Theme.cs` lists the themes (id, name, whether it's dark, its background, its date style). `ThemeSetting` keeps the one in use with the other settings, and `MainLayout` puts its id in `data-theme` on `<html>` and hands the theme to every component as a cascading value. The theme also goes into local storage, which `index.html` reads before the first paint.

- **Colors, fonts and line weights** are tokens in `src/AdaptiveWorkout.UI/wwwroot/app.css`, one block per theme under its `[data-theme]`. Fonts go by role: words, figures, labels, controls.
- **Shapes** that differ are rules under the theme's own `[data-theme]` (Telemetry's tiles, boxed fields, chips, filled tick boxes), so neither theme's rules touch the other's.
- **Pieces that draw differently** branch on the cascading theme inside their own component: the pen marks against Telemetry's checks (`Mark`), its rulers (`RulerTicks`, `SecondsRuler`, the rest and calorie rulers), the heart rate line, the workout header, Today's tiles, and the Gym chart.
- **The phone around the page** follows too (`PhoneThemeSetting`): the status and navigation bar icons go light on a dark theme, and the background behind the page while it loads matches. Both start in the last theme, kept in the phone's preferences. The splash and app icon stay Logbook's (vermilion on paper), since Android draws them before the app can choose.

To add a theme: add it to `Theme.All`, give it a token block (and any shapes) in `app.css`, add its fonts to `wwwroot/fonts`, and branch the components above where it should draw differently.

Fonts are in `src/AdaptiveWorkout.UI/wwwroot/fonts`, each under the SIL Open Font License (`OFL-*.txt`): Newsreader and Schibsted Grotesk for Logbook, Martian Mono and Atkinson Hyperlegible Next for Telemetry. The app icon and splash are SVGs in `src/AdaptiveWorkout.Phone/Resources`.

The watch wears the phone's theme too. The phone sends its theme's id with each workout, when the watch says hello, and when it changes; the watch keeps the last one. `src/AdaptiveWorkout.Watch/WatchTheme.cs` has each theme's colors (from the `app.css` tokens) and fonts: the same four, cut down to Latin as TTFs in `Resources/font` (Newsreader at a fixed optical size), with their licenses in `src/AdaptiveWorkout.Watch/OFL-*.txt`. Dimmed, the watch goes to light print on black, whatever the theme, to spare the screen. To add a theme there, add its `WatchTheme` and the fonts it needs.

## Tests

```
dotnet test --project tests/AdaptiveWorkout.Core.Tests
```

Tests are only for business logic in `Core`: no UI, wording, or plumbing tests.

## Why these choices

**Phone: .NET MAUI Blazor Hybrid.** MAUI is Microsoft's supported C# phone framework; it replaced Xamarin, which was retired in 2024. The Blazor Hybrid flavor writes the UI as Razor components (HTML/CSS) inside the native app. That's what makes a web version cheap later: move `Components/` into a Razor class library and host it from a Blazor web app too (the `maui-blazor-web` template shows the setup).

**Watch: C#, not Kotlin.** .NET for Android can build Wear OS apps (`dotnet new androidwear`), so the watch app stays in C# and shares `Core` with the phone. Kotlin with Jetpack Compose is Google's first-class path and has better samples. Switch only if the C# bindings get in the way.

**Getting heart rate off the watch**, best first:

| Option | Live? | Watch code? | Notes |
|---|---|---|---|
| Own watch app reads the HR sensor and sends it to the phone over the Wear OS Data Layer | Yes, about 1/sec | Yes (small) | **Built.** Sensor via Android `SensorManager` in a foreground service (Health Services' .NET binding is still prerelease); phone link via `Xamarin.GooglePlayServices.Wearable`. Both apps share an `ApplicationId` and signing key, which the Data Layer requires. The phone opens the watch app the way `RemoteActivityHelper` does (no .NET binding for it): a broadcast to the watch's companion app on the phone. |
| Third-party watch app (e.g. *Heart for Bluetooth*) that makes the watch act like a Bluetooth chest strap; phone reads the standard BLE Heart Rate service | Yes | No | Fastest "just a connector". The phone code would also work with any HR strap. Depends on someone else's app. |
| Health Connect on the phone (Samsung Health syncs into it) | No | No | Data arrives in batches, often roughly per minute and only after Samsung Health syncs. Fine for history, not for a live workout. |
| Samsung Health Sensor SDK | Yes | Yes | Needs Samsung partner approval. Skip. |

Heart rate permissions: Wear OS 6 (Android 16) replaces `BODY_SENSORS` with `android.permission.health.READ_HEART_RATE` for apps targeting API 36. The watch app declares both and asks for whichever the watch uses.

`Xamarin.GooglePlayServices.Wearable` is pinned to 119.0.0.4 in both apps: newer versions pull in AndroidX libraries newer than MAUI 10's, and the phone build fails on duplicate classes.

## Next steps

See **Build order** in [docs/concept.md](docs/concept.md).
