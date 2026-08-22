# About this project
* This project is developed for IVRC 2026, "Meal Be Back."
* In this project, the player experiences a reversal of eating sensations through a motor-controlled fluid device.
* The goal is to help the player recognize the importance of food and where it comes from, using a fish sausage as the demonstration food.
* Novelty (tentative): using a fluid (water) to express changes in food sensation.

* The fish sausage is virtualized by the 口腔内呈示部 (intraoral presentation unit). It consists of:
  * A replaceable outer cover (biocompatible, food-grade silicone/rubber) that contacts the player's mouth directly and expands/contracts to reproduce the volume change and biting resistance of the food.
  * A fluid control unit — a stepping-motor syringe pump — that inflates and deflates the cover.
    * ~~Servo-driven tube-pinch valves (SG90)~~ — **likely dropped** for the IVRC 2026 build due to budget/time constraints. Decision should be shared in the MTG on Aug final week.
  * ~~A chewing detection unit (surface EMG on the masseter)~~ — **dropped** for IVRC 2026 due to constraints. It will not be developed for this exhibition.

> **Open issue (blocking):** with sEMG cut, there is currently no defined input that tells the system when the player is chewing. Every other subsystem (visual, audio, fluid) was designed to be driven by that signal. A replacement trigger must be decided before the visual and fluid behavior can be specified. See "What this project handles" and "Goal of this project."

# What this project handles
* This Unity project handles the visual presentation (視覚呈示).
* First, the player is moved to the initialization scene (`SetupScene_SteamRuntime`), where an IVRC team member calibrates the desk height and prepares the player for the experience.
* Second, the player is moved to `WaterScene_SteamRuntime`, where the quiz experience begins. There, the player sees a desk in front of them; the scene resembles being underwater, with a visible coastline, god rays, and water. This scene is already implemented.

* `[TBD — to be decided by the team: the quiz interaction flow, what triggers the food-state change now that sEMG is cut, and how the intraoral device's fill level is driven during the quiz.]`

# Who will experience this project
* IVRC developers (Team "Tarberouters")
* IVRC judging team (admins)
* VRSJ members (attendees of VRSJ 2026)

# Why it is important
* The concept draws on the Nihon Shoki myth of Ukemochi no Kami, who is said to have produced rice, fish, and game from her mouth to entertain a guest. In Japan, gratitude toward food is also codified in the Basic Act on Shokuiku (食育基本法), and the phrase "itadakimasu" has increasingly been read as an acknowledgment of receiving another life. But even where this respect for life is institutionally and linguistically established, the step from a plated dish back to the living thing it came from is left to each person's imagination.
* This project aims to close that gap. By reversing the chewing process — so the food appears to un-chew back into the animal it came from, felt through the player's own mouth and matched with visuals and reversed chewing audio — the player is meant to confront, through their own body, the fact that eating means consuming a life.

# Goal of this project
* Deliver a food-education (食育) experience, structured as a quiz, in which the player guesses whether a food's raw material is a sea creature, then chews it to see the original creature emerge from their own mouth.
* Integrate the three channels
    — visual food-state change, reversed chewing sound, and intraoral expansion/contraction
    — into a single system so that the player's action and the presented sensation stay tightly coupled.
    * The 予稿 specified this coupling as being driven in real time by the player's own jaw motion. With sEMG dropped, that mechanism is currently undefined. Either a substitute input is chosen, or this goal is restated at a level the IVRC 2026 build can actually meet.

# When it should be completed
* By the IVRC 2026 exhibition (Sept 6–9, 2026, Toyama).

# Constraints (internal / for AI agents)
* **Budget:** roughly 5,000 JPY or less for hardware and system development. Everything must be built from scratch or assembled from cheap parts.
* **Time:** not enough to build something sophisticated. The bar for IVRC 2026 is "a system that works," not a refined one.
* **Skills:** development skill varies considerably across members. Task planning must account for this.


-----------
# Idea Memo — Meal Be Back (IVRC 2026)

Purpose: keep implementation-level ideas written down so they are not lost or mis-communicated.
Nothing here is final. Items are tagged; prune freely.

**Status tags**
* `[DECIDED]` — agreed, safe to build against
* `[LIKELY]` — leaning this way, not confirmed
* `[PROPOSAL]` — concrete enough to implement, not yet discussed
* `[SPECULATIVE]` — worth remembering, needs feasibility check first

---

## 1. Chewing timing: Wizard of Oz

### 1.1 Why `[DECIDED]`
sEMG is cut for IVRC 2026. A fully system-driven (open-loop) pacer means the device's volume and the player's actual jaw motion drift apart, and the drift is directly perceivable. An operator is already stationed at the booth for desk-height calibration, so a human can close the loop instead. Hardware cost: zero. Implementation cost: low.

### 1.2 Operator console `[PROPOSAL]`
Runs on the PC that already renders the HMD view. A second Unity Canvas on the desktop monitor, showing the mirrored player view plus current state.

Key bindings (keyboard, single hand, no mouse):

| Key | Action |
| --- | --- |
| `Space` | Chew beat — operator presses on each jaw closure |
| `R` | Re-sync — snap device fill and visual stage back to the current expected state |
| `N` | Advance to next reversion stage manually |
| `B` | Go back one stage (recovery from mispress) |
| `E` | Emergency stop — vent/retract fluid, freeze visuals |

Design rules:
* Every key must be idempotent or trivially reversible. The operator will mispress.
* No key should ever produce a large sudden pressure change in the mouth. `E` retracts, never fills.
* The console shows current stage, current fill %, and time since last beat, so a new operator can take over mid-shift.

### 1.3 Hybrid mode `[LIKELY]`
Pure WoZ requires the operator to watch continuously for the whole session. Practical compromise:
* The system runs a **default rhythm** (see §2) so the experience continues even if the operator's attention lapses.
* The operator's `Space` presses **phase-correct** that rhythm rather than triggering each beat from scratch — i.e. shift the phase of the running oscillator toward the observed press, clamped to some maximum shift per beat (e.g. 150 ms) so a single mispress cannot jerk the device.
* This degrades gracefully: with a perfect operator it behaves as closed-loop; with no operator it behaves as the open-loop pacer.

### 1.4 Operator script `[PROPOSAL]`
Write a one-page card for the booth. The operator is a team member who may not have run the experience before.
* What to watch (jaw line, not the HMD).
* What to say and when — kept minimal, ideally nothing during the experience itself.
* Failure recovery: if the player is clearly out of sync, press `R`, do not chase with `Space`.

---

## 2. Pacing without telling the player what to do

Core principle: the player should be **entrained** into the tempo, not instructed. Any on-screen "Please chew now" breaks the experience.

### 2.1 Channel priority `[PROPOSAL]`
This section should be considered well by HUMAN DEVELOPERS.
~~1. **The device itself is the metronome.** The cover inflating presses against the teeth; that physical push is the strongest possible "bite now" cue and needs no extra hardware. Deflation cues "open."~~
~~2. **Audio second.** Auditory rhythm entrainment in humans is much stronger than visual. Reversed chewing sound leads, the device follows.~~
~~3. **Visual is progress, not command.** Visuals show *how far along* the reversion is, never *when to act*.~~

~~Suggested offsets (tune by feel): audio at t=0, fluid motion at t≈+80–120 ms, visual stage change at t≈+150 ms. Slight lag reads as causality.~~

### 2.2 Making "chew slowly" implicit `[PROPOSAL]`
Never say it would be better in term of UX. Three mechanisms instead are suggested by Generative AI:
* **Start slow and never go fast.** ~1.5–2.0 s per chew cycle from the first beat. If a fast tempo is never demonstrated, slowness is the baseline rather than a restriction.
* **Make the cover physically hard to bite through.** Thicker wall / higher durometer material. This matches the 予稿's own design intent (§3.2: wall thickness and hardness express initial resistance and occlusal feel), and mechanically enforces slow chewing.
* **Use the first item as a tutorial.** Exaggeratedly slow for item 1 only. By item 2 the tempo is in the body.

---

## 3. Hiding open-loop drift

### 3.1 Discrete events, not continuous mapping `[PROPOSAL]`
Do **not** map jaw angle continuously to cover volume — a continuous mapping is falsified the instant it drifts. Map instead:

> 1 chew = 1 discrete reversion step

With discrete steps, a ±200 ms offset still reads as cause-and-effect. This is the single most important decision for making a WoZ / open-loop system feel responsive.

### 3.2 Few, large stages `[PROPOSAL]`
4–6 stages, each visually unmistakable. Example ladder for the fish sausage:

`sausage → paste (surimi) → minced flesh → fillet → whole fish → living fish`

The larger the per-step change, the lower the timing precision required.

### 3.3 Attribute mismatch to the fiction `[PROPOSAL]`
The premise is already non-physical — food is running backwards. Minor mismatches can be absorbed as part of the reversal effect rather than read as system error. Lean into this: make the reversion feel slightly *uncanny* rather than mechanically precise.

---

## 4. "A living fish in the mouth" `[SPECULATIVE]` — highest-value idea to check early

If the last reversion stage is not "a fish" but "a fish that is **alive**," the concept lands physically instead of only visually. The player would feel a life in their own mouth immediately before releasing it. This is arguably the payoff the whole project is aiming for.

### 4.1 Why it may be feasible with the hardware that survives the cuts
The stepping-motor syringe pump is a position-controlled actuator. It can produce not only slow fill/drain ramps but also small fast oscillations. "Alive" does not need large motion — it needs **small, irregular, self-initiated** motion.

### 4.2 Motion vocabulary `[PROPOSAL]`
Define named waveforms; the experience sequences them.

| Name | Character | Rough parameters |
| --- | --- | --- |
| `breathe` | slow, regular, barely perceptible | ~0.4–0.8 Hz, very small amplitude |
| `twitch` | single sharp flick | one fast step burst, <100 ms |
| `thrash` | struggling | 3–6 `twitch` events at irregular intervals over ~1 s |
| `still` | dead / inert | no motion |

Sequencing idea: `still` during early reversion stages → first isolated `twitch` at the "whole fish" stage (the moment the player realizes it moved) → `breathe` → `thrash` just before release.

### 4.3 The key perceptual cue is unpredictability `[PROPOSAL]`
A regular oscillation reads as a motor. An irregular one reads as an animal. Randomize inter-event intervals (e.g. draw from an exponential-ish distribution with a floor). Crucially, the motion must **not** be phase-locked to the chewing rhythm — the fish moving on its own schedule is exactly what makes it read as a separate agent.

### 4.4 Cross-modal reinforcement `[PROPOSAL]`
Each `twitch` fires simultaneously with: a visual jolt of the fish in VR, and a short wet/flapping sound. The haptic amplitude available is small, so the other two channels carry most of the impression. This is standard cross-modal amplification and it is cheap.

### 4.5 The release beat `[PROPOSAL]`
Strongest candidate for the ending: the player opens their mouth and the fish swims away into the sea. First-person, the fish leaves from just below the camera, crosses the view, joins the school. This is the image people will remember; protect its budget even if other things get cut.

### 4.6 Feasibility checks needed before committing
* Can the stepper actually produce a perceivable oscillation at the required frequency, given fluid compliance in the tube and cover? Compliance may low-pass-filter everything interesting. **Test this on the bench before designing around it.**
* Rigid tube and short tube length help. Water (incompressible) helps versus air.
* Hard pressure ceiling required — the amplitude that reads as "alive" must be far below anything uncomfortable. Small is fine here; the goal is subtlety, not force.
* Startle risk: a sudden motion inside the mouth can make someone bite down hard or gag. Ramp into it; the first `twitch` should be the smallest one.

---

## 5. Visual compensation

The device is weaker than the 予稿 assumed, so the visual channel carries more of the experience. Cheap wins, roughly in order of impact per hour of work.

* **Per-chew world response `[PROPOSAL]`** — every chew emits bubbles from just under the camera, a light flash, and a brief water distortion. When the world reacts to each bite, the weakness of the haptics is less salient.
* **Invest in the hero moment `[PROPOSAL]`** — §4.5. One well-made shot beats five mediocre ones.
* **URP Volume tuning `[PROPOSAL]`** — Bloom, Color Grading, underwater fog, caustics, god rays. `WaterScene_SteamRuntime` already exists, so this is parameter work, not new development.
* **Fish school `[PROPOSAL]`** — dozens of GPU-instanced fish. Free assets suffice. A sense of open ocean makes the absurdity of a desk on the seabed land harder.

---

## 6. Unity ↔ hardware interface `[PROPOSAL]`

Worth fixing early so the software and hardware sides can work in parallel with mocks.

Line-based ASCII over serial, newline-terminated. Unity is the master.

```
FILL <0-100>        set target fill percentage
WAVE <name>         start a named motion pattern (breathe|twitch|thrash|still)
STOP                halt motion, hold current position
VENT                retract to safe minimum
PING                → PONG (liveness check)
```

Rationale: ASCII is debuggable from a serial monitor by hand, which matters when members' skill levels vary. Firmware enforces its own safety limits (max fill, max rate, max pressure) regardless of what Unity sends — never trust the host.

Provide a **mock serial device** on the Unity side so the visual work is not blocked by hardware availability.

---

## 7. Open questions

* Does the fluid system have enough bandwidth for §4? (Blocking for the "living fish" idea.)
* Are the tube-pinch valves actually being dropped, and if so, does fill accuracy suffer enough to matter?
* Calibration procedure.
* How many food items in the quiz, and what are the non-seafood distractors?
* Cover cleaning/replacement turnaround between visitors — this sets the throughput of the booth and therefore the length of the experience.
* Who operates the WoZ console during the exhibition, and is one person enough for the full day?