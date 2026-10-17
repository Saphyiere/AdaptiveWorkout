# AdaptiveWorkout concept

The working spec for what the app does and how its data is shaped. Decisions are stated plainly; anything marked **(proposed)** is a suggestion still waiting for a yes or no. Open questions are collected at the end.

## What the app is

A personal data-collection app in the form of a workout tracker. One person, one phone, one Galaxy Watch.

**Right now, three things matter:**

1. **Did I go to the gym today?** Consistency.
2. **Was I under my calories today?**
3. **What do I weigh?**

**The long-term theme is adaptive.** Every time I do an exercise, the app should answer: *am I doing better than the last time I did it?* Which measure of "better" counts is my choice and comes later; the data to support it is recorded from day one.

Build the foundation first, then add on.

## Principles

- **Store raw facts with timestamps; calculate everything else.** Rest time, heart rate during a set, progress, consistency and calorie deficits are all derived, never stored. Those calculations are the business logic the tests cover.
- **Sessions are the center of workouts.** A session holds its sets and its heart rate. Calories and body weight happen on days, not in sessions, so they are simple daily logs.
- **No exercise library.** Exercises are whatever names I type. Typing a name I've used before links to it.
- **One button.** The app knows what's next and puts it on one big button. Everything else is one step away, never in the way.
- **Plain fields, no codes.** Weight and reps are typed into their own fields. Variations live in the exercise name.

## Words

| Word | Meaning |
|---|---|
| Routine | A named rotation of sessions, e.g. "Standard workout". |
| Session template | One named day in a routine, e.g. "Back day", with its exercises in order. |
| Session | One actual workout, from tapping Start to Finish workout. May come from a template or be "just tracking". |
| Exercise | A name I've typed, measured by **weight** (weight × reps) or by **time**. Variations are separate exercises: "flat chest press", "flat chest press dumbbells", "flat chest press machine". More measures can come later. |
| Set | One bout of an exercise: when it started and stopped, and what I did (weight × reps, or a duration). A 20-minute bike ride without stopping is one set. |
| Superset | Exercises done alternately as one block: pull-ups, dips, pull-ups, dips. |
| Rest | The gap between stopping one set and starting the next. Derived, never entered. |
| Gym day | A day with at least one session, whatever it was, a walk included. |

## Today

The home screen answers the three questions at a glance. **(proposed)**

- **Gym today:** done or not yet, plus this week's gym days (e.g. Mon, Wed, Thu).
- **Calories:** eaten today against 1,800, with a quick add.
- **Body weight:** the latest weigh-in and its trend, with a quick add.
- The big button: **Start Back day** (the next session in the rotation).

## Flows

### Start workout

The big button names the next session in the rotation: **Start Back day**. One tap starts it.

Next to it, one step away:
- **Pick a different session** from the routine.
- **Just track:** a session with no template; pick or create exercises as I go.
- **New routine or session:** type the routine's name, add sessions by name, and list each session's exercises (below).

With no routine yet, the button reads **Start workout** and starts a "just track" session; creating a routine is offered alongside.

The session's clock starts the moment I tap it. With a template, it goes straight to the first exercise (e.g. "Overhand rows") and its first set starts.

### Rotation

A routine is an ordered list of sessions, for example Push, Pull, Legs. The big button suggests the one after the last session I **finished**, wrapping around:

| Last finished | Suggested next |
|---|---|
| Push | Pull |
| Pull | Legs |
| Legs | Push |

It ignores weekdays: skip three days and it still suggests Legs after Pull. Nothing is ever "missed"; the next session simply waits. Doing a different session is always allowed, and the next suggestion follows whatever I actually finished.

### Listing a session's exercises

When creating a session template, I add exercises one by one: type a name and tap **Add**, or pick a matching exercise I've done before to link it. Each entry has:
- a **Sets** counter (− and +). "Auto" means as many sets as last time.
- up and down buttons to reorder, and a remove button.
- **Superset with the next exercise**, which joins it to the one below. The first exercise of a superset counts **Rounds** for all of them.

**(later)** With many exercises, a picker grouped into categories.

### Simple mode and advanced mode

Every session starts in **simple mode**: the screen holds the current step and one big button, and tapping through follows the plan. The plan is the template, or for exercises I've done before, what I did last time. A session I make up on the spot still gets simple mode for any exercise I link to past history. For the plan's order, see the next-step rules below.

I can leave simple mode at any moment, like leaving full screen. **Advanced mode** shows everything: switch to a different exercise, add an extra set, skip ahead, reorder, finish early. I can switch back and forth freely; simple mode picks up the plan from wherever I am. Simple and advanced are two views of the same session, not two kinds of session.

### The set loop

```
 Set running ──► Stop ──► Break (log the set here) ──► Start <next> ──► Set running
                                     │
                                     └──► Finish workout
```

1. **Set running.** Front and center:
   - **the set's timer**, the biggest thing on screen
   - live heart rate
   - **the last three times** I did this exercise, compactly:
     - `3 × 10 at 100`
     - `8, 9, 8 at 100`
     - `8 at 100 · 8 at 105 · 8 at 110`
   - the big **Stop** button
2. **Stop** ends the set, and rest starts counting.
3. **Break.** The rest timer, live heart rate, and the set's fields: weight and reps, pre-filled, both changeable on every set. Time exercises need nothing: the stopwatch was the duration. One big button names what's next: **Start set 3**, or **Start pull-ups**. Tapping it saves the set and starts the next one.
4. **Finish workout** is always reachable, in every state.

No rest countdown for now; a rest target may come later.

### Pre-filled and suggested values

The app is suggestive, especially in simple mode:
- Weight and reps pre-fill from the previous set in this session; the first set of an exercise pre-fills from the first set the last time I did it.
- **Hit goal:** every break screen has a **Hit goal** checkbox. Next time, that exercise's first set suggests the weight of the set I marked plus 5 (in that set's unit), and the following sets pre-fill from it as usual. Assisted weights move toward less assistance (−40 becomes −35); bodyweight stays bodyweight.

### Next-step rules

What the big button offers after each set:

1. If the template entry has a set count, move on after that many sets.
2. Else, if I've done this exercise before, move on after as many sets as last time.
3. Else, offer another set, with a second button to **move on to the next exercise**, or to **finish the workout** when it's the last one.

In a superset, the next step alternates through its exercises (pull-ups, dips, pull-ups, dips…) and a round counts as one set of each.

When nothing planned is left, the big button is **Finish workout**. When just tracking, the break screen always offers **Different exercise** and **Finish workout** under the big button.

### Speed

Every tap has to feel instant. The open workout lives in memory: Stop, Start and the break screen change on the spot, and saving happens in the background, one write at a time and in order. Buttons react the moment they're pressed, and each step slides in.

### Supersets

My back days are almost all supersets: a back exercise paired with a triceps exercise, like pull-ups and dips. Inside a superset there is no break: Stop on pull-ups goes straight to logging them and **Start dips**. The break comes after each round. The sets are stored like any other, in the order I did them; only the template knows which exercises are grouped. The gap between the two halves is still measured.

**(later)** Other patterns, like many sets of one rep, are just many sets; nothing special is needed.

### A new exercise

When I start a set of something that isn't in the session's template:

1. Type its name. Names I've used before are offered; picking one links to that exercise.
2. For a new name, choose **Weight** or **Time**.
3. The set starts.

### Logging after the fact

If I worked out without my phone, I can fill in a session afterward: pick the date, add exercises, and type weight × reps or a duration for each set. Such a session has no heart rate and no rest times, and is marked as logged afterward so the analysis can tell. It still counts as a gym day.

### Time-based exercises (planks, walking, cardio)

The stopwatch is the duration. Biking 5 minutes, resting 30 seconds and biking 5 more is two sets with a rest between, the same loop as lifting. A 20-minute ride without stopping is one set. Typing a duration is only for logging afterward.

## Adaptive: am I doing better?

My method: pick a weight and aim for three sets of 10. If I hit 10, 10, 10, the next time I do that exercise I go up in weight and aim for 10, 10, 10 again. Hitting every target is the win; missing reps means stay at that weight.

Sets vary (3 × 10 one day, 5 × 5 another, 2 × 6 then 2 × 12 at a lower weight), so each exercise is compared with the last session that had it on several signals, each up, flat or down. I choose which signal leads.

| Signal | Example |
|---|---|
| Heaviest weight | 100 → 105 lb |
| Reps at that weight | 10, 10, 8 → 10, 10, 10 |
| Total work (weight × reps, all sets) | 3,000 → 3,150 lb |
| Heart rate during sets, for the same work | avg 142 → 136 bpm |
| Time (time exercises) | 20:00 → 24:00 |

"Flat" over a stretch (the same weight × reps for a month) is a signal too: time to push.

**(later)** Suggestions from my method: "Last time 10, 10, 10 at 100. Try 105."

## Consistency

Derived from session dates alone:
- gym days per week, and weeks in a row with at least one gym day
- for a routine, how many of its sessions I did each week
- for time exercises like walking: total time per day and per week

## Weight

- Pounds by default, switchable to kilograms.
- An empty weight means bodyweight (push-ups).
- Negative weights are allowed, for assisted exercises (assisted pull-ups, assisted dips).
- **(proposed)** Store each weight exactly as entered (number + unit) and convert only for display and comparison, so a logged 100 lb never turns into 45.36 kg.

## Heart rate

The watch streams heart rate (about once a second) for the whole session, sets and breaks alike. The phone tells the watch when a session starts and finishes. From one stream the app derives:

- heart rate during each set (readings between the set's start and stop)
- heart rate while resting (readings between sets)
- the whole session's curve

How it runs:

- **Start** on the phone opens the heart rate screen on the watch; I never touch the watch. If the watch can't be opened directly, it gets a "Workout started" notification instead. With the watch screen off, that notification opens the heart rate screen on its own.
- **The watch screen:** the time up top, heart rate big in the middle, how long the workout has run below. When the watch screen times out, the app stays up, dimmed, instead of going back to the watch face. **(later)** A small graph or a zone scale under the number.
- The watch keeps reading with its screen off, and holds readings while the phone is out of reach, then sends them all.
- **The phone** shows the live number on the set and break screens, and saves every reading, even with the phone app in the background. Without a recent reading it says it's waiting and offers **Open on watch**.
- **Finish** (or cancelling an empty session) closes the watch app. If that message doesn't arrive, the next reading the phone gets for a finished workout sends it again, and the watch stops on its own after 4 hours.
- Opening the watch app by hand works too: it reads until I close it.

## Calories

- Just what I ate. Not tied to sessions.
- Enter one total before bed, or add amounts through the day; both are entries summed per day.
- Goal 1,800 a day; maintenance 2,200.
- A simple chart: eaten per day against the goal and maintenance lines.
- A day with no entries counts as over: maintenance + 200 (2,400). **(proposed)** The +200 is a setting.

## Body weight

A weigh-in whenever I step on the scale, charted over time.

## Data model

```
Routine            Id, Name
SessionTemplate    Id, RoutineId, Name, Order
TemplateExercise   TemplateId, ExerciseId, Order, PlannedSets (optional), SupersetGroup (optional)
Session            Id, TemplateId (empty = just tracking), StartedAt, FinishedAt, LoggedAfterward
Exercise           Id, Name (unique, ignoring case and extra spaces), Measure (Weight | Time)
Set                Id, SessionId, ExerciseId, Order,
                   StartedAt, StoppedAt         (empty when logged afterward)
                   Weight, WeightUnit, Reps     (weight exercises; empty weight = bodyweight, may be negative)
                   Duration                     (time exercises; from the stopwatch or typed)
                   HitGoal                      (checked on any break screen)
HeartRateSample    At, Bpm                      (matched to sessions, sets and rests by time)
FoodEntry          Id, At, Calories
BodyWeightEntry    Id, At, Weight, WeightUnit
Settings           CalorieGoal 1800, Maintenance 2200, UntrackedDayOver 200, Unit lb
```

**(proposed)** Stored locally on the phone (SQLite). Backup or Google sync later.

## Build order

1. **Today:** calories, weigh-ins and the gym-day check, with storage on the phone. *Built.*
2. **Sessions in simple mode:** routines, templates, the set loop, the last three times, supersets, Hit goal. *Built.*
3. **Live heart rate** from the watch during sessions. *Built; needs its first run on the real watch.*
4. **Advanced mode** and logging after the fact. *Advanced view built (add a set, different exercise, skip); reordering mid-workout and logging after the fact not yet.*
5. **Adaptive comparisons** and charts.

## Later

- Goals and suggestions: a target per exercise (sets × reps), "try 105 today".
- Choosing which "better" signal leads.
- Weekday schedules for routines.
- Rest timer targets.
- More exercise measures (distance, reps only).
- A categorized exercise picker.
- Macros.
- Cloud backup with a Google account.

## Open questions

None right now.
