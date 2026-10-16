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
- **Plain fields, no codes.** Weight and reps are typed into their own fields. Variations live in the exercise name.

## Words

| Word | Meaning |
|---|---|
| Routine | A named rotation of sessions, e.g. "Standard workout". |
| Session template | One named day in a routine, e.g. "Push", with its exercises in order. |
| Session | One actual workout, from tapping Start to Finish workout. May come from a template or be "just tracking". |
| Exercise | A name I've typed, measured by **weight** (weight × reps) or by **time**. Variations are separate exercises: "flat chest press", "flat chest press dumbbells", "flat chest press machine". More measures can come later. |
| Set | One bout of an exercise: when it started and finished, and what I did (weight × reps, or a duration). |
| Rest | The gap between finishing one set and starting the next. Derived, never entered. |

## Flows

### Start workout

1. I tap **Start workout**.
2. If a session is still open, resume it.
3. Else, if I have a routine, start the next session in its rotation: the one after the last session I finished, wrapping around. **(proposed)** Order-based by default; fixed weekdays (Mon/Wed/Fri) can come later.
4. Else ask: **Use a routine** or **Just track**?
   - **Just track:** a session with no template. Pick or create exercises as I go.
   - **Use a routine:** type the routine's name, then add a session and type its name. Tapping the session starts it.

The session's clock starts the moment I tap it. If its template has exercises, it goes straight to the first one and that set starts.

### The set loop

```
 Start session ──► Set running ──► Done ──► Resting ──┬─► Next ─────────────────► Set running
                                                      ├─► Different exercise ───► Set running
                                                      └─► Finish workout
```

1. **Set running.** The set's clock is running and its heart-rate window is open. The screen shows the exercise and its fields:
   - **Weight:** weight and reps, pre-filled from my last set of this exercise.
   - **Time:** a stopwatch counting from the set's start.
2. I do the set, adjust the fields if needed, and tap **Done**.
3. **Resting** starts immediately and counts up. The rest screen has:
   - **Next**, the big button: the smart choice. Another set of the same exercise, or the next exercise in the template once this one is done.
   - **Different exercise:** pick another exercise from the template or anywhere else, for when a machine is taken or plans change.
   - **Finish workout.**
4. **Finish workout** is always on screen, in every state.

No rest countdown for now; a rest target may come later.

### A new exercise

When I start a set of something that isn't in the session's template yet:

1. Type its name. Names I've used before are offered; picking one links to that exercise.
2. For a new name, choose **Weight** or **Time**.
3. The set starts.

### Logging after the fact

If I worked out without my phone, I can fill in a session afterward: pick the date, add exercises, and type weight × reps or a duration for each set. Such a session has no heart rate and no rest times, and is marked as logged afterward so the analysis can tell.

### Time-based exercises (planks, cardio)

The stopwatch is the duration: **Done** stops it. Biking 5 minutes, resting 30 seconds and biking 5 more is two sets with a rest between, the same loop as lifting. Typing a duration is only for logging afterward.

## Weight

- Pounds by default, switchable to kilograms.
- Negative weights are allowed, for assisted exercises (assisted pull-ups, assisted dips).
- **(proposed)** Bodyweight exercises (push-ups) leave weight empty.
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
- A day with no entries counts as maintenance (2,200). **(proposed)** A setting can make untracked days count as over instead.

## Body weight

**(proposed)** A weigh-in whenever I step on the scale, charted over time next to calories.

## Data model

```
Routine            Id, Name
SessionTemplate    Id, RoutineId, Name, Order
TemplateExercise   TemplateId, ExerciseId, Order
Session            Id, TemplateId (empty = just tracking), StartedAt, FinishedAt, LoggedAfterward
Exercise           Id, Name (unique, ignoring case and extra spaces), Measure (Weight | Time)
Set                Id, SessionId, ExerciseId, Order,
                   StartedAt, FinishedAt        (empty when logged afterward)
                   Weight, WeightUnit, Reps     (weight exercises; weight may be negative or empty)
                   Duration                     (time exercises; from the stopwatch or typed)
HeartRateSample    SessionId, At, Bpm
FoodEntry          Id, At, Calories
BodyWeightEntry    Id, At, Weight, WeightUnit
Settings           CalorieGoal 1800, Maintenance 2200, Unit lb
```

**(proposed)** Stored locally on the phone (SQLite). Backup or Google sync later.

## Later

- What makes a set or session "good": progression rules, heart-rate targets.
- Planned set counts and targets in templates.
- Weekday schedules for routines.
- Rest timer targets.
- More exercise measures (distance, reps only).
- Macros.
- Cloud backup with a Google account.

## Open questions

1. **Smart Next:** when does Next move on to the next exercise? Options: after the same number of sets as last time, after a planned set count in the template, or never on its own (Next = another set; Different exercise moves on).
2. **Filling templates:** does a session template learn its exercises from the last time I did that session, or do I list them when creating it?
3. **Rotation:** is "the next session after the last one I finished" right, and can I override it before starting ("not today, do Legs")?
4. **Bodyweight exercises:** leave weight empty, or something else?
5. **Untracked calorie days:** count as maintenance, or as over?
6. **Body weight:** confirm it's a simple weigh-in log with a chart.
