# AdaptiveWorkout concept

The working spec for what the app does and how its data is shaped. Decisions are stated plainly; anything marked **(proposed)** is a suggestion still waiting for a yes or no. Open questions are collected at the end.

## What the app is

A personal data-collection app in the form of a workout tracker. One person, one phone, one Galaxy Watch. It exists for three things:

1. **Did I work out right?** Effort judged from heart rate and from the weights I lift compared with what I lifted before. Example: bench press at the same weight for sets of 10 all month counts as flat. *Rules come later; the data to support them comes first.*
2. **Track everything:** heart rate, sets and workouts, cardio, calories eaten.
3. **Track body weight.**

Build the foundation first, then add on.

## Principles

- **Store raw facts with timestamps; calculate everything else.** Rest time, heart rate during a set, progress, weekly patterns and calorie deficits are all derived, never stored. Those calculations are the business logic the tests cover.
- **Sessions are the center of workouts.** A session holds its sets and its heart rate. Calories and body weight happen on days, not in sessions, so they are simple daily logs.
- **No exercise library.** Exercises are whatever names I type. Typing a name I've used before links to it.
- **One button.** "Start workout" should know what to do. It only asks questions when it can't work it out.

## Words

| Word | Meaning |
|---|---|
| Routine | A named rotation of sessions, e.g. "Standard workout". |
| Session template | One named day in a routine, e.g. "Push". What it plans (exercises) comes later. |
| Session | One actual workout, from first set to Finish. May come from a template or be "just tracking". |
| Exercise | A name I've typed, e.g. "flat chest press". Measured by weight × reps or by time. |
| Set | One bout of an exercise: a start time, an end time, and what I did (weight × reps, or a duration). |
| Rest | The gap between finishing one set and starting the next. Derived, never entered. |

## Flows

### Start workout

1. I tap **Start workout**.
2. If a session is still open, resume it.
3. Else, if I have a routine, start the next session in its rotation: the one after the last session I finished, wrapping around. **(proposed)** Order-based by default; fixed weekdays (Mon/Wed/Fri) can come later.
4. Else ask: **Use a routine** or **Just track**?
   - **Just track:** a session with no template. Log sets as they come.
   - **Use a routine:** type the routine's name, then add a session and type its name. Tapping the session starts it.

### The set loop

```
 Start set ──► In a set ──► Finish set ──► Resting ──► Next set ─┐
    ▲                                         │                  │
    └─────────────────────────────────────────┼──────────────────┘
                                              ▼
                                       Finish workout
```

1. **Start set.** The set's clock starts and its heart-rate window opens. I pick the exercise: start typing, and names I've used before are offered; picking one links to it, a new name creates a new exercise.
2. I do the set.
3. I enter the weight and reps (pre-filled from my last set of that exercise) and tap **Finish set**.
4. **Resting** starts immediately and counts up. The screen offers **Next set** and **Finish workout**.
5. **Next set** starts the next set (same exercise pre-selected, changeable).
6. **Finish workout** is always on screen, in every state.

The session's start time is the moment the first set starts. **(proposed)** No rest countdown for now; a rest target may come later.

### Time-based exercises (planks, cardio)

An exercise is measured by **weight × reps** or by **time**, chosen when it's first created. For time-based ones, the set's duration is simply Start set → Finish set, so nothing needs typing. Biking 5 minutes, resting 30 seconds and biking 5 more is two sets with a rest between, the same loop as lifting.

## Set notation

My journal notation, used to type a set quickly or log a whole exercise after the fact, and to show history compactly:

```
flat chest press 100 10 10 10        three sets of 10 at 100 lb
flat chest press 100 10 10; 110 8 8  weight changes after ";"
curls 30s 12 10 10                   "s" after the weight = dumbbells (30 lb each)
```

- Everything before the first weight is the exercise name.
- A group is a weight followed by one or more rep counts; `;` starts a new group.
- Weights can have decimals (47.5).

**(proposed)** Time and bodyweight, which the old notation couldn't express cleanly (`planks 30s 1 1 1` read like dumbbells):

```
planks 0:30 0:30 0:30                durations as m:ss, no weight
pushups bw 20 20 15                  "bw" = bodyweight
weighted planks 25 0:45 0:45         a weight, then durations
```

Parsing this is exactly the kind of logic the tests are for.

## Heart rate

The watch streams heart rate (about once a second) for the whole session, sets and rests alike. The phone tells the watch when a session starts and finishes. From one stream the app derives:

- heart rate during each set (readings between the set's start and end)
- heart rate while resting (readings between sets)
- the whole session's curve

## Calories

- Just what I ate. Not tied to sessions.
- Enter one total before bed, or add amounts through the day; both are entries summed per day.
- Goal 1,800 a day; maintenance 2,200.
- A simple chart: eaten per day against the goal and maintenance lines, so the trend shows at a glance.
- A day with no entries counts as maintenance (2,200). **(proposed)** A setting can make untracked days count as over instead.

## Body weight

**(proposed)** A weigh-in whenever I step on the scale, charted over time next to calories.

## Units

Pounds by default, switchable to kilograms. **(proposed)** Store each weight exactly as entered (number + unit) and convert only for display and comparison, so a logged 100 lb never turns into 45.36 kg.

## Data model

```
Routine           Id, Name
SessionTemplate   Id, RoutineId, Name, Order
Session           Id, TemplateId (empty = just tracking), StartedAt, FinishedAt
Exercise          Id, Name (unique, ignoring case and extra spaces), Measure (WeightReps | Time)
Set               Id, SessionId, ExerciseId, StartedAt, FinishedAt,
                  Weight, WeightUnit, Dumbbells (yes/no), Reps        (weight × reps)
                  Duration comes from StartedAt..FinishedAt          (time)
HeartRateSample   SessionId, At, Bpm
FoodEntry         Id, At, Calories
BodyWeightEntry   Id, At, Weight, WeightUnit
Settings          CalorieGoal 1800, Maintenance 2200, Unit lb
```

**(proposed)** Stored locally on the phone (SQLite). Backup or Google sync later.

## Later

- What makes a set or session "good": progression rules, heart-rate targets.
- Session templates that plan exercises.
- Weekday schedules for routines.
- Rest timer targets.
- Macros.
- Cloud backup with a Google account.

## Open questions

1. **Exercise change:** on the rest screen, is it **Next set** (same exercise, changeable) and **Finish workout**? Or do you want a separate **Next exercise** button?
2. **Notation in the live flow:** do you type `100 10` into one box during a set, or use separate weight and reps fields, with the full notation for after-the-fact logging only?
3. **Session start:** does the session's clock start at the first set, as written above, or when you tap the session?
4. **Rotation:** is "the next session after the last one I finished" right, and can I override it ("not today, do Legs")?
5. **Time and bodyweight notation:** are `0:30` for durations and `bw` for bodyweight OK?
6. **Dumbbells:** is 30s (two 30 lb dumbbells) compared only with other dumbbell sets of that exercise, never with barbell weights?
7. **Untracked calorie days:** count as maintenance, or as over?
8. **Body weight:** confirm it's a simple weigh-in log with a chart.
