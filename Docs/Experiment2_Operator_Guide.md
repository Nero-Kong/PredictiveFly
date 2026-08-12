# Experiment 2 Operator Guide

## Design

- Participants: 12 new participants, identified as `E2P001` to `E2P012`.
- Delays: 500 ms and 1000 ms.
- Proxy strategies at each delay: Current, Experiment-1 Fixed predictive, and Personalized predictive.
- Formal trials: six per participant. Delay order, strategy order, and route are assigned automatically from Participant ID and Trial Index.
- Current uses zero additional prediction beyond the real-time remote estimate. Fixed reuses the Experiment 1 profile at both delays: translation 0.08-0.45 s and yaw 0.05-0.20 s.
- Calibration: the personalized maximum translation horizon is calibrated separately for each delay over 0-1.0 s. It is additional look-ahead beyond the Current proxy, so a participant may reasonably select a smaller value at 1000 ms or select zero. The prediction controller continues to apply its confidence, acceleration, 3 m spatial lead, and collision constraints; effective applied windows are recorded in the objective time series.

## Before Each Participant

1. Open `Assets/Scenes/IrairaBou3D_Official.unity`.
2. Select `IrairaBou3D Official Scene Builder`.
3. On `PredictiveFlyExperiment2Controller`, set `Participant Id` once and set `Trial Index` to 1.
4. Keep the Inspector hidden from the participant so numeric horizons and condition labels are not disclosed.
5. Enter Play Mode and keep the same Play session for calibration and all six formal trials.

## Personalized Calibration

1. Press `F7` once to start the full calibration sequence.
2. The participant presses `[` to shorten and `]` to lengthen the prediction horizon.
3. After at least 20 seconds of use, the participant presses `Space` to accept the run.
4. The controller automatically runs low- and high-anchor repetitions for both delays. It adds a mid-anchor repetition when the first two accepted values differ by more than 0.2 s.
5. Wait until the Inspector status reports `Calibration complete`.

The calibration event CSV is updated after every adjustment and acceptance, so a crash does not erase the adjustment history.

## Formal Trials

1. Confirm the desired `Trial Index` from 1 to 6.
2. Press `Enter`. The controller rebuilds the assigned route if needed, applies the scheduled delay and strategy, fills the delay buffer, starts all four objective CSV streams, and then enables movement.
3. Completing the course closes and saves the four CSV files, disables movement, and leaves the experiment paused.
4. Complete any between-trial questionnaire, set the next `Trial Index`, and press `Enter` again.
5. After trial 6, the status reports that the Experiment 2 session is complete.

Press `Escape` only to abort an active calibration or formal trial. An aborted formal trial is saved with `completed=0` under the `Incomplete` subdirectory and must be repeated using the same Trial Index.

## Output

- Completed objective trials: `Data/PredictiveFlyExperiment2/Objective`
- Incomplete objective trials: `Data/PredictiveFlyExperiment2/Objective/Incomplete`
- Calibration choices and adjustment history: `Data/PredictiveFlyExperiment2/Calibration`

Each formal trial produces the existing four objective CSV files. Filenames include participant ID, Experiment 2 condition, timestamp, and trial number. Condition metadata also records both calibrated horizons, the fixed prediction profile, route order, strategy order, and delay.

## Recovery

Play Mode values do not persist after leaving Play Mode. If Unity must be restarted after calibration, read the final `sequence_complete` row from the participant's calibration CSV and manually restore:

- `Calibrated Participant Id`
- `Selected Horizon 500 Seconds`
- `Selected Horizon 1000 Seconds`

Then resume the unfinished Trial Index. Never reuse calibration values with a different Participant ID.

## Validation

With Play Mode stopped, run `PredictiveFly > Experiment 2 > Validate Schedule And Scene`. The validator checks all 12 participant schedules, delay and calibration-anchor balance, per-delay strategy-order balance, condition-route balance, controller ownership, logger policy, and scene references.
