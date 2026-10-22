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

- **Store raw facts with timestamps; calculate everything else.** Rest time, heart rate during a set, progress, consistency and calorie deficits are all derived, never stored. Those calculations are the business logic the tests cover. The one exception is each workout's **improvement**: it's worked out once, right after Finish, and kept with the workout, so the trend never has to redo it.
- **Sessions are the center of workouts.** A session holds its sets and its heart rate. Calories and body weight happen on days, not in sessions, so they are simple daily logs.
- **No exercise library.** Exercises are whatever names I type. Typing a name I've used before links to it.
- **One button.** The app knows what's next and puts it on one big button. Everything else is one step away, never in the way.
- **Plain fields, no codes.** Weight and reps are typed into their own fields. Variations live in the exercise name.

## Words

| Word | Meaning |
|---|---|
| Routine | A named week, e.g. "Six-day split": a session or a rest day for each day. One is in use, or none. |
| Session template | A named list of exercises in order, e.g. "Back day". Any routine can use it on any days. |
| Session | One actual workout, from tapping Start to Finish workout. May come from a template or be "just tracking". |
| Exercise | A name I've typed, measured by **weight** (weight × reps) or by **time**. Variations are separate exercises: "flat chest press", "flat chest press dumbbells", "flat chest press machine". More measures can come later. |
| Set | One bout of an exercise: when it started and stopped, and what I did (weight × reps, or a duration). A 20-minute bike ride without stopping is one set. |
| Superset | Exercises done alternately as one block: pull-ups, dips, pull-ups, dips. |
| Rest | The gap between stopping one set and starting the next. Derived, never entered. |
| Warm-up, cool-down | Time at the start and end of a workout, with heart rate and nothing to enter. Each session turns them on or off. |
| Gym day | A day with at least one session, whatever it was, a walk included. |

## Today

The home screen answers the three questions at a glance. **(proposed)**

- **Gym today:** done, not yet, or **rest day**, plus this week's gym days (e.g. Mon, Wed, Thu) with the routine's rest days marked, and the streak. Tapping it opens **Gym**.
- **Calories:** eaten today against 1,800, with a quick add. **Edit** lists today's entries, each deletable.
- **Body weight:** the latest weigh-in and its trend, with a quick add. **Edit** lists the last 10 weigh-ins, each deletable.
- The big button: **Start Back day** (today's session in the routine). On a rest day, or once today is done, it reads **Start a workout** and opens the session picker. On a rest day, **Edit routine** sits under it.

Every delete asks once (Keep or Delete) before anything is gone.

### Gym

Am I getting better, and everything I've done:

- **Improvement:** how much better my exercises are than the first time I did them, on average (**+12.8%** over 13 workouts), with the line of how it got there. Tapping the line picks a workout: its name, date and how it did against the last time. See *Adaptive* for how it's worked out.
- **Edit routine** opens the routines, to edit one or switch.
- **History:** every finished workout, newest first and grouped by month: its name, date, length, set count and improvement, with a delete on each row. Tapping one shows its numbers (time, sets, volume, average and highest heart rate) and every set by exercise: weight × reps, how long the set took, and the heart rate during it. Tapping a set opens it to change its weight and reps (or time) and Hit goal, or to delete just that set after asking once; that workout's improvement, and every later one's, is worked out again. A warm-up comes before the exercises and a cool-down after them, each with how long it took and its average and highest heart rate. **Delete workout** at the bottom removes it and its sets, so suggestions and the last three times forget it too.

### Settings

The icon at the top right of Today. For now it holds the look:

- **Logbook:** a printed training log, warm paper and a red pen.
- **Telemetry:** a dark instrument panel, with every number on a ruler.

Choosing one switches the whole app at once, and it's remembered.

### Getting around

Every screen below Today has its way back at the top, naming the screen it goes back to, and the phone's back gesture does the same: one screen per step, back the way I came. Finishing what a screen was opened for (saving a session or routine, deleting a workout, choosing in Change) also goes back there, never on to a fresh copy of a screen I'd already left, so back never walks through finished forms. Starting a workout from the session picker, and switching the workout between its views, take the screen's place instead of stacking up. Two or more screens in, **Today** at the top right (a house in Telemetry) goes straight home, and finishing or cancelling a workout always lands on Today.

## Flows

### Start workout

The big button names today's session in the routine: **Start Back day**. One tap starts it.

Next to it, one step away (**Other session**), the session picker:
- **The routine in use** at the top, with its week; tap it to switch routines or edit one.
- **Any session**, whatever the routine says (today's is marked).
- **Just track:** a session with no template; pick or create exercises as I go.
- **New session:** name it and list its exercises (below).

With no routine, the button reads **Start a workout** and opens the picker, and **Set up a routine** sits under it.

The session's clock starts the moment I tap it. With a template, it goes straight to the first exercise (e.g. "Overhand rows") and its first set starts, unless the session starts with a warm-up (see *Warm-up and cool-down*).

While a workout is open, the session picker shows it at the top with **Resume**, **Finish** and **Cancel session**. Cancel deletes the workout and its sets, after asking once.

### Routines

A routine plans the week: each day has a session or is a rest day. Monday to Saturday with a different session each day and Sunday off is one routine; a five-day week is another. Sessions don't belong to a routine, so the same Chest day can sit in several, on any days.

- One routine is **in use**, or none. Switching is one tap in the routines list; editing a routine changes its name and days, and can put it in use.
- Editing a routine has **New session** under the week. Saving the new session comes back to the routine as I left it, changes not saved yet included, with the new session ready to pick for its days.
- Today's session comes from the routine in use. Doing a different session is always allowed.
- **Rest days are part of the plan:** Today says so, and they never break the streak.
- **No routine** is fine too: any day can be a gym day, and the streak counts weeks.

### Listing a session's exercises

When creating a session template, I add exercises one by one: type a name and tap **Add**, or pick a matching exercise I've done before to link it. Each entry has:
- a **Sets** counter (− and +). "Auto" means as many sets as last time.
- up and down buttons to reorder, and a remove button.
- **Superset with the next exercise**, which joins it to the one below. The first exercise of a superset counts **Rounds** for all of them.

**Warm-up first** sits above the exercises and **Cool-down last** below them, where each one happens.

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

   A superset's round runs both exercises at once, split side by side: each one's name with its own **Change**, one timer, and each one's last three times under it.
2. **Stop** ends the set, and rest starts counting.
3. **Break.** The rest timer, live heart rate, and an entry for each set since the last rest (one, or both halves of a superset round): weight, reps and **Hit goal**, pre-filled, each with its own **Save** (Enter works too). Time exercises need nothing: the stopwatch was the duration. Once every entry is saved, the break becomes a plain rest screen: **Today so far**, every exercise done in this workout in short form (`3 × 10 at 100`), with **Edit** to see today's sets one by one: tapping a set changes its numbers or Hit goal, or deletes it (a set done by mistake), after asking once. What's next follows from the sets left. One big button at the bottom names what's next: **Start set 3**, or **Start pull-ups**. It only starts the next set; anything not saved yet keeps what's in its fields.
4. **Finish workout** is always reachable, in every state.

The break screen reads **Rest 0:48**: the word big beside the timer, counting up. **Tapping the time** turns it into a countdown (and back); the choice sticks for later rests.

- **It learns:** the countdown after an exercise starts from the one I set for it last time; never set, from how long I usually rested after it last time (the middle rest, to the nearest 15 seconds); with nothing to go on, 1:30. **−15s** and **+15s** adjust it, and that's remembered.
- **At zero** the watch buzzes, and the next set starts by itself. The phone buzzes and beeps instead only when no watch is connected (no heart rate in the last 15 seconds), so they never both go. The last ten seconds turn red. After the last exercise, a cool-down waits for a tap.
- With the watch on, the watch keeps the time: at zero it starts the next set on the phone wherever the phone is, in a pocket with its app asleep included. The phone checks the countdown the watch heard is still this rest's, so one that changed meanwhile starts nothing.
- Without a watch, starting by itself and the phone's buzz need the app open on the break screen (the phone's screen can be off). A countdown that ran out while I was on another screen, or while the phone slept, doesn't start anything; it shows how far over I am (**+0:23**).

**The notification:** the workout also sits in the notification bar and on the lock screen, so the phone can stay in a pocket: **Rest** with the countdown (or the time since the set, counting up) and what's next, or the running set and its time. The system keeps the timer ticking without the app. On Android 16 and newer it's a Live Update, which puts the timer in a chip in the status bar. Tapping it opens the app.

### Pre-filled and suggested values

The app is suggestive, especially in simple mode:
- Weight and reps pre-fill from the previous set in this session; the first set of an exercise pre-fills from the first set the last time I did it.
- **Hit goal:** every set's entry on the break screen has a **Hit goal** checkbox. Next time, that exercise's first set suggests the weight of the set I marked plus 5 (in that set's unit), and the following sets pre-fill from it as usual. Assisted weights move toward less assistance (−40 becomes −35); bodyweight stays bodyweight.

### Next-step rules

What the big button offers after each set:

1. It stays with the exercise I'm on. With a set count in the template entry, it moves on after that many sets.
2. Else, if I've done this exercise before, it moves on after as many sets as last time.
3. Else it offers another set, with a second button to **move on to the next exercise**, or to **finish the workout** when it's the last one. Such an exercise is done once a later one in the plan had a set after its last.
4. Moving on means the next exercise in the plan after this one. Ones I went past (swapped out, dropped, skipped, or left for another exercise I picked) never come back on their own; they stay in the lists of what's still to do, so I can go back to one by picking it.
5. Once it moves on, a second button offers **Another set of rows** (or **Another round**) for when I'm doing more than planned.

Each place in the plan counts its own sets: an exercise swapped into another one's place starts at set 1 there, even if I did it earlier in a superset.

When nothing planned is left, the big button is **Finish workout**, with **Another set** and **Add more** above it. When just tracking, the break screen always offers **Different exercise** and **Finish workout** under the big button. With a cool-down, each of those reads **Start cool-down** instead.

### Warm-up and cool-down

A session can start with a warm-up and end with a cool-down, each turned on in its list of exercises. Both are only time and heart rate: nothing to enter.

- **Warm-up:** Start opens it in place of the first set: the time since Start, live heart rate, and what's next with **Change**. The big button, **Start overhand rows**, ends it and starts the first set. The watch and the notification show **Warm-up** like a rest, so the watch's **Start overhand rows** works too.
- **Cool-down:** where the big button would finish the workout, it reads **Start cool-down**. The cool-down shows its time and live heart rate, and its big button is **Finish workout**. A set started during it (another exercise, or another set from advanced mode) puts the cool-down off until the end again.
- **Finish** in the masthead still finishes at once, with no cool-down. Finishing during the warm-up makes the whole workout warm-up.
- Advanced mode lists them as the first and last boxes of the plan; **Today so far** starts with the warm-up's time.

### Changing today's workout

Machines are taken all the time, so a workout never has to follow its session exactly. Each workout gets its own copy of the session's exercises when it starts; changing it never touches the session.

**Change** sits on the set screen and next to "Next:" on the break screen. It opens for that exercise:

- **Do another one first:** any exercise still to do. The changed one waits and comes right after (the bench is taken, so incline first; the pulldown is taken, so skull crushers, then back to the pulldown). A superset moves as a whole. Part way through an exercise, the sets still to do come right after the other one.
- **Swap it for** any other exercise, today only, keeping its spot, sets and superset. The one swapped out is gone for today. One still to do later in the plan moves up instead; one already done today goes in afresh.
- **Drop it today** (no dip station at this gym: the superset becomes just pull-ups, with the same rounds). There's no skip button anywhere else; changing the plan should take a deliberate step.

In a superset, each exercise has its own **Change**. From the set screen, the running set (or round) starts over with the new exercise.

**Different exercise** in advanced mode starts something in place of what's next: a planned one (with its superset), or one the plan doesn't have, which joins the plan right after what's next. Either way the plan carries on after it, and what it took the place of waits in the list of what's still to do. **Add more**, once everything planned is done, adds a whole other session (an ab session after the bike) or one exercise after everything else.

### Speed

Every tap has to feel instant. The open workout lives in memory: Stop, Start and the break screen change on the spot, and saving happens in the background, one write at a time and in order. Buttons react the moment they're pressed, and each step slides in, already mostly visible so the slide never reads as waiting. Debug builds run compiled code too (the Android interpreter is off), so testing feels like the real thing.

Opening has to be quick, and stay quick as the log grows. Today reads only what it shows: the list of workouts for the week and the streak, today's food, and the last 30 days of weigh-ins (plus the latest few for the list). The workout history behind suggestions and the last three times, every set ever logged, loads once Today is up, so it never holds Today up and is usually ready before Start or Resume needs it. The history screens read only what they show too: a past workout reads its own sets and the heart rate during it, and Gym reads how many sets each workout had of each exercise rather than every set.

### Supersets

My back days are almost all supersets: a back exercise paired with a triceps exercise, like pull-ups and dips, or skull crushers straight into rows. I go from one to the other without stopping, so a round is one activity: **Start pull-ups + dips** starts both, the screen shows both side by side, and one **Stop** ends the round. The break comes after each round, and that's where both are entered. Each round stores a set of each exercise, in order, with the round's start and stop, so both get the same time and the same heart rate. Only the template knows which exercises are grouped. Swapping one of them part way keeps the round count: the new one joins the next round.

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

### Improvement (the default lead signal)

Until I choose a lead signal, one number stands for "better", so the Gym trend has something to show:

- **Each exercise** counts as its sets' average estimated one-rep max, weight × (1 + reps ÷ 30), so heavier and more reps both count, however the sets were split (10, 10, 8 → 10, 10, 10 at 100 is +1.7%; 100 → 105 is +5%). Bodyweight and assisted sets lift my body too, at my last weigh-in before the workout, the same for both workouts (−50 → −40 assist at 200 lb is +6.7%); with no weigh-in, plain bodyweight compares by reps alone. Time exercises count their average set length.
- **A workout's improvement** is how its exercises did against the last time I did each, averaged (the geometric mean, so +10% and the same ratio down even out). Exercises done for the first time are left out; with none to compare, a workout has no improvement. It's worked out once after Finish and stored; workouts finished before this existed get theirs the first time Gym opens.
- **The trend** is each exercise's change since its first time, averaged over every exercise compared so far. More workouts in a week don't inflate it: bench +10% and curls +10% is +10%.

## Consistency

Derived from session dates alone:
- gym days per week, out of the routine's planned days
- the streak: with a routine, gym days in a row, where a planned day I skipped ends it, rest days never do, a workout on one still counts, and today only counts once I've been; with no routine, weeks in a row with at least one gym day
- for time exercises like walking: total time per day and per week

## Weight

- Pounds by default, switchable to kilograms.
- An empty weight means bodyweight (push-ups).
- Negative weights are allowed, for assisted exercises (assisted pull-ups, assisted dips).
- **(proposed)** Store each weight exactly as entered (number + unit) and convert only for display and comparison, so a logged 100 lb never turns into 45.36 kg.

## Heart rate

The watch streams heart rate (about once a second) for the whole session, sets and breaks alike. The phone tells the watch when a session starts and finishes. From one stream the app derives:

- heart rate during each set (readings between the set's start and stop)
- heart rate during the warm-up and the cool-down
- heart rate while resting (readings between sets)
- the whole session's curve

How it runs:

- **Start** on the phone opens the heart rate screen on the watch; I never touch the watch. If the watch can't be opened directly, it gets a "Workout started" notification instead. With the watch screen off, that notification opens the heart rate screen on its own.
- **The watch screen** wears the phone's theme, colors and fonts (Logbook's paper and ink, or Telemetry's dark panel and lime): the time up top, then what the workout is doing (**Rest 1:12** counting down, or the set and its time), heart rate big in the middle, and how long the workout has run. Its button is the phone's big button, in the workout time's place: **Stop** while a set or a superset's round runs, **Start pull-ups** (or **Start cool-down**) between them. Finishing stays on the phone. When the watch screen times out, the app stays up, dimmed to light print on black, for the whole workout instead of going back to the watch face: the workout is an ongoing activity, whose icon on the watch face also taps straight back in. **(later)** A small graph or a zone scale under the number.
- The phone tells the watch what the workout is doing on every change: a set started or stopped, a rest countdown turned on or adjusted.
- **Rest is up:** when the countdown hits zero, the watch buzzes, starts the next set on the phone, and, if its app isn't showing, comes up on its own with the set running. The phone's app doesn't need to be open: the watch keeps the time, and the phone hears it in the background. Out of the phone's reach it still buzzes, and shows **Rest is up +0:05** with **Start pull-ups** for when the phone is back.
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
RoutineDay         RoutineId, Day (Monday 0 to Sunday 6), TemplateId (empty = rest day)
SessionTemplate    Id, Name, Order, WarmUp, CoolDown
Setting            Key, Value                    (the routine in use, the theme, rest countdowns)
TemplateExercise   TemplateId, ExerciseId, Order, PlannedSets (optional), SupersetGroup (optional)
SessionExercise    SessionId, ExerciseId, Order, PlannedSets, SupersetGroup   (the workout's own copy, changed as it goes)
Session            Id, TemplateId (empty = just tracking), StartedAt, FinishedAt, LoggedAfterward
                   WarmUp, WarmUpEndedAt         (copied from the template at Start; warming up from StartedAt until the first set)
                   CoolDown, CoolDownStartedAt   (cooling down from Start cool-down until FinishedAt)
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
3. **Live heart rate** from the watch during sessions, with the rest timer on the watch and in the notification. *Built; needs its first run on the real watch.*
4. **Advanced mode** and logging after the fact. *Advanced view built (add a set, different exercise, skip); reordering mid-workout and logging after the fact not yet.*
5. **Adaptive comparisons** and charts. *The improvement trend on Gym is built; choosing the lead signal and per-exercise charts not yet.*

## Later

- Goals and suggestions: a target per exercise (sets × reps), "try 105 today".
- Choosing which "better" signal leads.
- More exercise measures (distance, reps only).
- A categorized exercise picker.
- Macros.
- Cloud backup with a Google account.

## Open questions

None right now.
