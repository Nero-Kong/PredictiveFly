# Residual BC Pipeline (HeadOffset Dynamic Baseline)

This folder contains an offline **residual behavior-cloning** pipeline:

- Baseline controller: dynamic rule built into `AdaptiveFlyAgent` (same formulation)
- Learner target: `a_residual = a_real - a_rule`
- Dataset source: `AdaptiveFly/Data/Exp1/*AdaptiveFly*.csv`

## 1) Build residual dataset

```powershell
.\.venv-ml\Scripts\python.exe OfflineTraining\ResidualBC\build_residual_dataset.py `
  --data-dir "C:\Users\54402\OneDrive\Desktop\AdaptiveFly\Data\Exp1" `
  --mode-keyword AdaptiveFly `
  --val-subjects P011,P012
```

Output:

- `OfflineTraining/ResidualBC/data/exp1_adaptivefly_residual_dataset.npz`
- `OfflineTraining/ResidualBC/data/exp1_adaptivefly_residual_summary.json`

## 2) Train offline residual model

```powershell
.\.venv-ml\Scripts\python.exe OfflineTraining\ResidualBC\train_residual_bc.py `
  --dataset OfflineTraining\ResidualBC\data\exp1_adaptivefly_residual_dataset.npz `
  --output-dir OfflineTraining\ResidualBC\artifacts `
  --epochs 120 `
  --batch-size 1024 `
  --device cuda
```

Output:

- `OfflineTraining/ResidualBC/artifacts/residual_bc_best.pt`
- `OfflineTraining/ResidualBC/artifacts/residual_bc_final.pt`
- `OfflineTraining/ResidualBC/artifacts/residual_bc.onnx`
- `OfflineTraining/ResidualBC/artifacts/training_summary.json`

## 3) Unity side setting for residual RL fine-tuning

`AdaptiveFlyAgent` now supports:

- `Policy Output Mode = ResidualOnDynamicRule`
- `Residual Action Scale` controls how strong the learned residual is

Recommended for PPO fine-tuning:

1. Keep `Policy Output Mode = ResidualOnDynamicRule`
2. Start with `Residual Action Scale = 0.25~0.35`
3. Keep `Use Dummy Feature Source = false` when tuning with real head input
4. Use existing PPO config (`TrainingConfigs/adaptive_fly_ppo.yaml`) and run resume/new training

## Notes

- The offline ONNX here is a plain residual regressor (`obs -> residual`) for analysis/pretraining.
- ML-Agents `Behavior Parameters` expects ML-Agents-exported model format for direct drop-in inference.
- Practical path: use this offline step to verify residual target learnability, then do online PPO in residual mode for deployment.
