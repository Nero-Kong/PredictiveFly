# AdaptiveFly RL (Residual + Dynamic Baseline)

## Runtime (Inference)
1. Select `XRRig` (locomotion root).
2. Add/keep components on the same object:
   - `HeadIntentFeatureExtractor`
   - `Behavior Parameters`
   - `AdaptiveFlyAgent`
3. `AdaptiveFlyAgent` key settings:
   - `Policy Output Mode = ResidualOnDynamicRule`
   - `Residual Action Scale = 0.25~0.35` (start from 0.3)
   - `Use Dummy Feature Source = false`
4. `Behavior Parameters`:
   - `Behavior Name = AdaptiveFly`
   - `Behavior Type = InferenceOnly` (or `Default` for training)
   - `Model = ML-Agents exported onnx`

## Training (PPO fine-tuning)
1. Keep `Policy Output Mode = ResidualOnDynamicRule`.
2. Use existing config:
```powershell
.\.venv-ml\Scripts\python.exe -m mlagents.trainers.learn TrainingConfigs\adaptive_fly_ppo.yaml --run-id AdaptiveFly_Intent_v1 --resume --timeout-wait 300
```
3. In Unity press Play.

## Offline residual BC pretraining
See `OfflineTraining/ResidualBC/README.md`.

The offline pipeline uses:
- input: 17-dim observation aligned with `AdaptiveFlyAgent`
- label: residual action (`real - dynamic_rule`)
- baseline: built-in dynamic mapping inside `AdaptiveFlyAgent`

Note:
- Offline `residual_bc.onnx` is a plain regressor (`obs -> residual`) for analysis/pretraining.
- Deployment model for `Behavior Parameters` should come from ML-Agents export after PPO fine-tuning.
