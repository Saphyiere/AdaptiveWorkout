# AdaptiveWorkout concept

The working spec for what the app does and how its data is shaped. Decisions are stated plainly; anything marked **(proposed)** is a suggestion still waiting for a yes or no. Open questions are collected at the end.

## What the app is

A personal data-collection app in the form of a workout tracker. One person, one phone, one Galaxy Watch.

**The theme is adaptive.** Every time I do an exercise, the app should answer one question: *am I doing better than the last time I did it?* Better can mean any of:

- the same weight and reps with a lower heart rate
- the same weight with more reps (finishing out at 10)
- a higher weight with the same, similar or slightly fewer reps

Alongside it sits **consistency**: am I showing up? Even if the only workout in a week is a walk, the app tracks how much I walked and whether I keep doing it.

It also tracks what surrounds the workouts: calories eaten and body weight.

Build the foundation first, then add on.

## Principles

- **Store raw facts with timestamps; calculate everything else.** Rest time, heart rate during a set, progress, consistency and calorie deficits are all derived, never stored. Those calculations are the business logic the tests cover.
- **Sessions are the center of workouts.** A session holds its sets and its heart rate. Calories and body weight happen on days, not in sessions, so they are simple daily logs.
- **No exercise library.** Exercises are whatever names I type. Typing a name I've used before links to it.
- **One button.** "Start workout" should know what to do. It only asks questions when it can't work it out, and I can always override it.
- **Plain fields, no codes.** Weight and reps are typed into their own fields. Variations live in the exercise name.

## Words

| Word | Meaning |
|---|---|
| Routine | A named rotation of sessions, e.g. "Standard workout". |
| Session template | One named day in a routine, e.g. "Push", with its exercises in order. |
| Session | One actual workout, from tapping Start to Finish workout. May come from a template or be "just tracking". |
| Exercise | A name I've typed, measured by **weight** (weight × reps) or by **time**. Variations are separate exercises: "flat chest press", "flat chest press dumbbells", "flat chest press machine". More measures can come later. |
| Set | One bout of an exercise: when it started and finished, and what I did (weight × reps, or a duration). A 20-minute bike ride without stopping is one set. |
| Rest | The gap between finishing one set and starting the next. Derived, never entered. |

## Adaptive: am I doing better?

My method: pick a weight and aim for three sets of 10. If I hit 10, 10, 10, the next time I do that exercise I go up in weight and aim for 10, 10, 10 again. Hitting every target is the win; missing reps means stay at that weight.

Because sets vary (3 × 10 one day, 5 × 5 another, 2 × 6 then 2 × 12 at a lower weight), the comparison can't assume the same shape every time. **(proposed)** For each exercise, compare this session with the last session that had it, on several signals, and show each one as up, flat or down:

| Signal | Example |
|---|---|
| Heaviest weight | 100 → 105 lb |
| Reps at that weight | 10, 10, 8 → 10, 10, 10 |
| Total work (weight × reps, all sets) | 3,000 → 3,150 lb |
| Heart rate during sets, for the same work | avg 142 → 136 bpm |
| Time (time exercises) | 20:00 → 24:00 |

"Flat" over a stretch (the same weight × reps for a month) is a signal too: time to push.

**(later)** Suggestions from my method: "Last time 10, 10, 10 at 100. Try 105." That needs a target per exercise (sets × reps) in the template.

## Consistency

**(proposed)** Derived from session dates alone:
- sessions per week, and weeks in a row with at least one session
- for a routine, how many of its sessions I did each week
- for time exercises like walking: total time per day and per week

## Flows

### Start workout

1. I tap **Start workout**.
2. If a session is still open, resume it.
3. Else, if I have a routine, it suggests the next session in the rotation (see Rotation below), and I can pick a different one before starting.
4. Else ask: **Use a routine** or **Just track**?
   - **Just track:** a session with no template. Pick or create exercises as I go.
   - **Use a routine:** type the routine's name, then add a session and type its name, then list its exercises (below). Tapping the session starts it.

The session's clock starts the moment I tap it. If its template has exercises, it goes straight to the first one and that set starts.

### Rotation

A routine is an ordered list of sessions, for example Push, Pull, Legs. Start workout suggests the one after the last session I **finished**, wrapping around:

| Last finished | Suggested next |
|---|---|
| Push | Pull |
| Pull | Legs |
| Legs | Push |

It ignores weekdays: skip three days and it still suggests Legs after Pull, picking up where I left off. Doing a different session is always allowed, and the next suggestion follows whatever I actually finished.

### Listing a session's exercises

When creating a session template, I add exercises one by one: type a name and tap **Add**, or pick an existing exercise from the dropdown to link it. Each entry has an optional **set counter** (a + button). Next to an entry with no count, a hint says Next will keep adding sets until I move on.

**(later)** With many exercises, a picker grouped into categories.

### The set loop

```
 Start session ──► Set running ──► Done ──► Resting ──┬─► Next ─────────────────► Set running
                                                      ├─► Different exercise ───► Set running
                                                      └─► Finish workout
```

1. **Set running.** The set's clock is running and its heart-rate window is open. The screen shows the exercise and its fields:
   - **Weight:** weight and reps. Every set can change both.
   - **Time:** a stopwatch counting from the set's start.
2. I do the set, adjust the fields if needed, and tap **Done**.
3. **Resting** starts immediately and counts up. The rest screen has:
   - **Next**, the big button (Smart Next, below).
   - **Different exercise:** pick another exercise from the template or anywhere else, for when a machine is taken or plans change.
   - **Finish workout.**
4. **Finish workout** is always on screen, in every state.

No rest countdown for now; a rest target may come later.

**(proposed)** Pre-filled fields: from the previous set in this session; for the first set of an exercise, from the first set the last time I did it.

### Smart Next

Next decides between another set of the same exercise and the next exercise in the template:

1. If the template entry has a set count, move on after that many sets.
2. Else, if I've done this exercise before, move on after as many sets as last time.
3. Else, Next is always another set; **Different exercise** moves on.

**(proposed)** That order of priority.

### A new exercise

When I start a set of something that isn't in the session's template:

1. Type its name. Names I've used before are offered; picking one links to that exercise.
2. For a new name, choose **Weight** or **Time**.
3. The set starts.

### Logging after the fact

If I worked out without my phone, I can fill in a session afterward: pick the date, add exercises, and type weight × reps or a duration for each set. Such a session has no heart rate and no rest times, and is marked as logged afterward so the analysis can tell.

### Time-based exercises (planks, walking, cardio)

The stopwatch is the duration: **Done** stops it. Biking 5 minutes, resting 30 seconds and biking 5 more is two sets with a rest between, the same loop as lifting. A 20-minute ride without stopping is one set. Typing a duration is only for logging afterward.

## Weight

- Pounds by default, switchable to kilograms.
- An empty weight means bodyweight (push-ups).
- Negative weights are allowed, for assisted exercises (assisted pull-ups, assisted dips).
- **(proposed)** Store each weight exactly as entered (number + unit) and convert only for display and comparison, so a logged 100 lb never turns into 45.36 kg.

## Heart rate

The watch streams heart rate (about once a second) for the whole session, sets and rests alike. The phone tells the watch when a session starts and finishes. From one stream the app derives:

- heart rate during each set (readings between the set's start and finish)
- heart rate while resting (readings between sets)
- the whole session's curve

## Calories

- Just what I ate. Not tied to sessions.
- Enter one total before bed, or add amounts through the day; both are entries summed per day.
- Goal 1,800 a day; maintenance 2,200.
- A simple chart: eaten per day against the goal and maintenance lines, so the trend shows at a glance.
- A day with no entries counts as over: maintenance + 200 (2,400). **(proposed)** The +200 is a setting.

## Body weight

A weigh-in whenever I step on the scale, charted over time.

## Data model

```
Routine            Id, Name
SessionTemplate    Id, RoutineId, Name, Order
TemplateExercise   TemplateId, ExerciseId, Order, PlannedSets (optional)
Session            Id, TemplateId (empty = just tracking), StartedAt, FinishedAt, LoggedAfterward
Exercise           Id, Name (unique, ignoring case and extra spaces), Measure (Weight | Time)
Set                Id, SessionId, ExerciseId, Order,
                   StartedAt, FinishedAt        (empty when logged afterward)
                   Weight, WeightUnit, Reps     (weight exercises; empty weight = bodyweight, may be negative)
                   Duration                     (time exercises; from the stopwatch or typed)
HeartRateSample    SessionId, At, Bpm
FoodEntry          Id, At, Calories
BodyWeightEntry    Id, At, Weight, WeightUnit
Settings           CalorieGoal 1800, Maintenance 2200, UntrackedDayOver 200, Unit lb
```

**(proposed)** Stored locally on the phone (SQLite). Backup or Google sync later.

## Later

- Goals and suggestions: a target per exercise (sets × reps), "try 105 today".
- Weekday schedules for routines.
- Rest timer targets.
- More exercise measures (distance, reps only).
- A categorized exercise picker.
- Macros.
- Cloud backup with a Google account.

## Open questions

1. **Rotation:** does "the session after the last one I finished, ignoring weekdays" match how you train? Or should a routine be able to pin sessions to weekdays (Push on Monday)?
2. **Smart Next order:** set count first, then "as many as last time", then "another set"?
3. **"Better" signals:** are the five signals in the Adaptive table the right ones? Which one matters most to you?
4. **Pre-filled fields:** from the previous set in this session, and for the first set, from last time's first set?
5. **Consistency:** what counts as consistent for you: any session that week, or completing the routine?
