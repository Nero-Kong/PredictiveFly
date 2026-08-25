# PredictiveFly Experiment Protocol

This document describes the experiment implemented by the Unity project. Freeze this document and the Unity commit before confirmatory data collection.

## Conditions

- A: `RealTimeDroneBody` (`NoDelay`)
- B: `DelayedDroneBody` (`DelayedFeedback`)
- C: `DelayedWithRealTimeGhost` (`Delayed_CurrentAvatar`)
- D: `DelayedWithPredictiveGhost` (`Delayed_PredictiveAvatar`)

The controller uses a balanced Latin square:

```text
ABDC
BCAD
CDBA
DACB
```

The trailing positive number in `participantId` selects the condition row. The operator sets `trialIndex` from 1 to 4; the controller selects the corresponding condition automatically.

Routes use the same four balanced orders in independent four-participant blocks. Within every block of four participants, every condition is paired with every route once. Route order rotates across a 16-participant cycle and is recorded as `route_order` in every CSV.

## Before Each Trial

On `IrairaBou3D Official Scene Builder > PredictiveFlyExperimentController`:

1. Enable only `PredictiveFlyExperimentController`; disable the Experiment 2 and Experiment 3 controllers.
2. Set `participantId`.
3. Set `trialIndex` to 1, 2, 3, or 4.
4. Ask the participant to stand in a neutral posture with the left body-anchor controller tracked.
5. Press Enter once. The controller assigns the condition and route, rebuilds the route when needed, normalizes the course material state, freezes input, recenters, fills the delay buffer, validates body tracking, starts in-memory objective-data buffering, and enables input.

Controller interlocks are intentionally disabled. Experiment 1 and Experiment 3 both use Enter, so they must not be enabled together. Do not switch experiment controllers during an active run.

Press Escape to abort. Reaching the course finish writes all four CSV files and then locks locomotion so the participant can rest. Change only `trialIndex`, then press Enter when ready for the next trial. A completed participant/trial pair cannot be repeated accidentally during the same Play Mode session.

If a trial is aborted, its buffered data is discarded and no CSV files are created. Keep the same `trialIndex` and press Enter to repeat it.

## Valid Trial Rules

A trial is invalid when any of the following occurs:

- Body-anchor tracking is unavailable for 5 seconds or longer.
- The condition changes after logging starts.
- The logger stops before finish.
- The participant or experimenter aborts.
- A Unity or XR error prevents completion.

No objective CSV files are produced for an invalid trial. Document the exclusion reason in the experimenter's separate session log before repeating the same `trialIndex`.

## Objective Reference State

All task metrics use the real-time remote collision probe reconstructed from the real-time remote root pose and the tracked probe offset. Delayed HMD and delayed drone-body transforms are logged separately and are not used for completion, path length, obstacle clearance, or collision calculation.

Route progress and reference distance use the `IrairaBouTubeBoundary` centerline. Path efficiency is:

```text
reference route distance / measured remote-probe path length
```

## Course Visibility

Every condition and route uses the same Fresnel-enhanced transparent wall, one continuous white top guide, two cyan dashed side guides, and 6 m distance rings. The guides are rendered directly by the wall shader and have no colliders or triggers. Distance rings are visual-only objects without colliders. None of these cues changes the route centerline, tube boundary, obstacles, checkpoints, finish detection, or objective probe.

## CSV Files

- `objective_timeseries.csv`: 30 Hz state, input, delay, prediction, tracking, route progress, obstacle, and blocking data.
- `objective_events.csv`: trial, tracking, checkpoint, finish, collision, near-miss, blocking, and avoidance events.
- `objective_trial_summary.csv`: one trial-level outcome row.
- `objective_obstacle_encounters.csv`: one row per obstacle encounter.

Data for all four files is buffered in memory during flight. The files are written as one completed set only after the real-time remote probe reaches the course finish. Stopping Play Mode, pressing Escape, tracking failure, a mode change, or any other pre-finish stop discards the entire buffered set.

Every file freezes participant, trial, route, route order, condition order, condition label, mode, and configuration ID at trial start.

## Confirmatory Analysis

Recommended primary contrast: D vs. C.

Recommended primary objective outcome: participant-level mean minimum clearance for designated obstacle encounters. Analyze encounter-level observations with participant as a repeated/random effect; obstacle rows are not independent participants.

Treat collisions and near misses as secondary safety outcomes. Correct secondary planned contrasts with Holm correction.

## Pilot Checklist

- Verify no files appear during flight and all four matching files appear after finish.
- Verify checkpoint count and route progress increase monotonically.
- Deliberately touch a wall and an avoidance obstacle; confirm collision events.
- Deliberately approach and turn away; confirm exactly one first avoidance-onset event.
- Disconnect the body anchor; confirm automatic abort after the 5-second grace period and confirm that no files are created.
- Check frame time, task duration, sickness, ghost visibility, and prediction-horizon range.
- Freeze delay, prediction, locomotion, obstacle, and logging parameters after the pilot.
