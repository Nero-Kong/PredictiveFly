# Experiments 2 and 3 Operator Guide

## Protocol Overview

- Experiment 2 protocol: `E2_5delay_preference_v1`.
- Experiment 3 protocol: `E3_3x2_personalized_v1`.
- Use the same Participant ID in both components, for example `P001`.
- Experiment 2 measures the participant's preferred additional prediction horizon.
- Experiment 3 tests whether that saved horizon improves full-course performance over Current.

The two controllers are independent components on `IrairaBou3D Official Scene Builder`:

1. `PredictiveFlyExperiment2Controller` runs preference calibration only.
2. `PredictiveFlyExperiment3Controller` runs measured course trials only.

Both may remain enabled. Their controls do not overlap, and each controller blocks its own movement while the other is active.

## Experiment 2: Preferred Horizon

Experiment 2 presents five system delays:

- 0 ms
- 250 ms
- 500 ms
- 750 ms
- 1000 ms

Delay order follows a ten-sequence balanced Williams design selected automatically from the numeric suffix of Participant ID. With 12 participants, the first two sequences repeat; this is the closest balanced allocation possible for five delays and 12 participants.

For each delay, the participant completes a low-anchor and high-anchor adjustment run. A third mid-anchor run is added when the first two accepted values differ by more than 0.2 s. The locked value is the rounded mean of two runs or median of three runs. A selected horizon of 0 s is valid.

### Controls

- `F7` or `K`: start the complete Experiment 2 sequence.
- `Left Arrow`: reduce the additional prediction horizon.
- `Right Arrow`: increase the additional prediction horizon.
- `Space`: accept the current run after the minimum exposure time.
- `Escape`: abort Experiment 2. No completed profile is produced.

### Procedure

1. Set Participant ID on `PredictiveFlyExperiment2Controller`.
2. Enter Play Mode and press `F7` or `K` once.
3. Let the participant adjust the horizon while moving through both straight and curved portions.
4. Press `Space` only after the participant is satisfied and the minimum run time has elapsed.
5. Continue until all five delays are complete.
6. Confirm that the Inspector reports `Calibration complete and saved locally`.

Calibration events are written incrementally, but Experiment 3 unlocks only when a completed JSON profile and its matching CSV both exist.

### Output

- Directory: `Data/PredictiveFlyExperiment2/Calibration`
- Event file: `<ParticipantId>_<time>_experiment2_calibration.csv`
- Completed profile: `<ParticipantId>_E2_5delay_preference_v1_completed.json`

The profile stores selected horizons for all five delays plus the prediction method and profile-shape parameters.

## Experiment 3: Performance Validation

Experiment 3 is a `3 delays x 2 proxy strategies` within-participant design:

| Delay | Current | Personalized Future |
| --- | --- | --- |
| 0 ms | Additional horizon `H=0` | `H=H*(0)` from Experiment 2 |
| 500 ms | Additional horizon `H=0` | `H=H*(500)` from Experiment 2 |
| 1000 ms | Additional horizon `H=0` | `H=H*(1000)` from Experiment 2 |

Questionnaire mode codes are fixed as follows:

| Delay | Current | Personalized |
| --- | --- | --- |
| 0 ms | `A_1` | `A_2` |
| 500 ms | `B_1` | `B_2` |
| 1000 ms | `C_1` | `C_2` |

Open `Window > PredictiveFly > Experiment 3 Questionnaire Mode`, or use the
`Open Questionnaire Mode Window` button in the Experiment 3 Inspector. The
window updates automatically and reopens when a completed trial is ready for
its questionnaire. A Personalized trial keeps its `_2` code even when the
participant selected `H=0`, because the code identifies the assigned strategy.

There are six full-course trials. Delay-block order, Current/Personalized order, and route assignment are selected automatically from Participant ID. The 12-participant base schedule balances all six conditions across trial positions and route variants.

### Procedure

1. Complete Experiment 2 first, either in the same or an earlier Unity session.
2. Set the identical Participant ID on `PredictiveFlyExperiment3Controller`.
3. Set Trial Index to 1.
4. Press `Enter`. The controller rechecks the completed Experiment 2 files before every trial.
5. The participant completes the assigned full course.
6. Read the questionnaire mode code from the Experiment 3 window. After a rest
   and questionnaire, set the next Trial Index and press `Enter` again.
7. Continue through Trial Index 6.

Movement remains locked until `Enter` successfully prepares the trial. Pressing `Escape`, losing required tracking, stopping Play Mode, or ending before the finish discards the buffered objective data. Only trials that reach the course finish retain the four objective CSV files.

### Output

- Completed trials: `Data/PredictiveFlyExperiment3/Objective`
- No incomplete-trial CSV files are retained.

Each completed trial uses the existing four-file objective format. Configuration metadata includes the questionnaire mode code, both protocol versions, delay, strategy, selected horizon, all five Experiment 2 horizons, route order, condition order, and calibration filenames.

## Moving Between Computers

Copy both of these for every calibrated participant:

- The completed Experiment 2 JSON profile.
- The exact Experiment 2 calibration CSV named inside that profile.

Place them in the same `Data/PredictiveFlyExperiment2/Calibration` directory on the destination computer. Experiment 3 will remain locked if either file is missing, the Participant ID differs, or the protocol version is incompatible.

## Validation

With Play Mode stopped, run:

`PredictiveFly > Experiments 2 and 3 > Validate Schedule And Scene`

The validator checks the five-delay Experiment 2 order and anchor balance, the six-condition Experiment 3 schedule, route balance, prediction semantics, completed-profile gate, completed-only objective policy, scene references, and removal of the prediction-distance cap.
