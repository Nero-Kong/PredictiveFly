#!/usr/bin/env python
"""Train an offline residual behavior cloning model and export ONNX.

Expected NPZ keys from build_residual_dataset.py:
- train_obs, train_target, train_rule, train_real
- val_obs, val_target, val_rule, val_real
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Dict, Tuple

import numpy as np
import torch
from torch import nn
from torch.utils.data import DataLoader, Dataset


class ResidualDataset(Dataset):
    def __init__(self, obs: np.ndarray, target: np.ndarray, rule: np.ndarray):
        self.obs = torch.from_numpy(obs.astype(np.float32))
        self.target = torch.from_numpy(target.astype(np.float32))
        self.rule = torch.from_numpy(rule.astype(np.float32))

    def __len__(self) -> int:
        return self.obs.shape[0]

    def __getitem__(self, idx: int):
        return self.obs[idx], self.target[idx], self.rule[idx]


class ResidualMLP(nn.Module):
    def __init__(self, obs_dim: int = 17, hidden: int = 256, residual_clip: float = 0.6):
        super().__init__()
        self.residual_clip = residual_clip
        self.net = nn.Sequential(
            nn.Linear(obs_dim, hidden),
            nn.ReLU(inplace=True),
            nn.Linear(hidden, hidden),
            nn.ReLU(inplace=True),
            nn.Linear(hidden, hidden // 2),
            nn.ReLU(inplace=True),
            nn.Linear(hidden // 2, 4),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return torch.tanh(self.net(x)) * self.residual_clip


def resolve_device(preferred: str) -> torch.device:
    if preferred == "cuda" and torch.cuda.is_available():
        return torch.device("cuda")
    if preferred == "cpu":
        return torch.device("cpu")
    return torch.device("cuda" if torch.cuda.is_available() else "cpu")


def masked_mse(x: torch.Tensor, mask: torch.Tensor) -> torch.Tensor:
    if mask.sum() <= 0:
        return x.new_tensor(0.0)
    return (x[mask] ** 2).mean()


def evaluate(model: nn.Module, loader: DataLoader, device: torch.device) -> Dict[str, float]:
    model.eval()
    mae_res = 0.0
    mae_total = 0.0
    stop_drift = 0.0
    n = 0

    with torch.no_grad():
        for obs, target, rule in loader:
            obs = obs.to(device)
            target = target.to(device)
            rule = rule.to(device)

            pred = model(obs)
            pred_total = rule + pred
            gt_total = rule + target

            batch = obs.shape[0]
            n += batch
            mae_res += torch.mean(torch.abs(pred - target), dim=1).sum().item()
            mae_total += torch.mean(torch.abs(pred_total - gt_total), dim=1).sum().item()

            low_intent = obs[:, -1] < 0.12
            if torch.any(low_intent):
                drift = torch.sqrt(torch.sum(pred_total[low_intent] ** 2, dim=1))
                stop_drift += drift.mean().item() * int(low_intent.sum().item())

    out = {
        "mae_residual": mae_res / max(n, 1),
        "mae_total_action": mae_total / max(n, 1),
        "low_intent_action_mag": stop_drift / max(n, 1),
    }
    return out


def train(args: argparse.Namespace) -> None:
    data = np.load(args.dataset)

    train_obs = data["train_obs"]
    train_target = data["train_target"]
    train_rule = data["train_rule"]

    val_obs = data["val_obs"]
    val_target = data["val_target"]
    val_rule = data["val_rule"]

    if train_obs.shape[1] != 17:
        raise ValueError(f"Expected obs dim 17, got {train_obs.shape[1]}")

    train_ds = ResidualDataset(train_obs, train_target, train_rule)
    val_ds = ResidualDataset(val_obs, val_target, val_rule)

    train_loader = DataLoader(train_ds, batch_size=args.batch_size, shuffle=True, drop_last=False)
    val_loader = DataLoader(val_ds, batch_size=args.batch_size, shuffle=False, drop_last=False)

    device = resolve_device(args.device)
    model = ResidualMLP(obs_dim=17, hidden=args.hidden, residual_clip=args.residual_clip).to(device)

    optimizer = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=args.weight_decay)
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(optimizer, T_max=max(args.epochs, 1), eta_min=args.lr * 0.1)

    # Weight vertical/yaw channels more, since they are often weaker in collected data.
    dim_weights = torch.tensor([1.0, 1.0, 1.35, 1.2], device=device).view(1, 4)

    best_val = float("inf")
    best_epoch = -1
    history = []

    out_dir = args.output_dir
    out_dir.mkdir(parents=True, exist_ok=True)
    best_pt = out_dir / "residual_bc_best.pt"

    for epoch in range(1, args.epochs + 1):
        model.train()
        running = 0.0
        steps = 0

        for obs, target, rule in train_loader:
            obs = obs.to(device)
            target = target.to(device)
            rule = rule.to(device)

            pred = model(obs)
            pred_total = rule + pred

            huber = torch.nn.functional.smooth_l1_loss(pred, target, reduction="none")
            loss_res = (huber * dim_weights).mean()

            low_intent = obs[:, -1] < args.low_intent_threshold
            loss_hold = masked_mse(pred_total, low_intent)

            loss_reg = torch.mean(torch.sum(pred * pred, dim=1))

            loss = loss_res + args.low_intent_weight * loss_hold + args.residual_l2_weight * loss_reg

            optimizer.zero_grad(set_to_none=True)
            loss.backward()
            nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            optimizer.step()

            running += float(loss.item())
            steps += 1

        scheduler.step()

        train_loss = running / max(steps, 1)
        val_metrics = evaluate(model, val_loader, device)
        score = val_metrics["mae_total_action"] + args.low_intent_metric_weight * val_metrics["low_intent_action_mag"]

        history.append(
            {
                "epoch": epoch,
                "train_loss": train_loss,
                **val_metrics,
                "score": score,
                "lr": optimizer.param_groups[0]["lr"],
            }
        )

        if score < best_val:
            best_val = score
            best_epoch = epoch
            torch.save(
                {
                    "model_state": model.state_dict(),
                    "epoch": epoch,
                    "score": score,
                    "args": vars(args),
                },
                best_pt,
            )

        if epoch % args.log_every == 0 or epoch == 1 or epoch == args.epochs:
            print(
                f"[Epoch {epoch:04d}] "
                f"train_loss={train_loss:.6f} "
                f"val_mae_total={val_metrics['mae_total_action']:.6f} "
                f"val_mae_res={val_metrics['mae_residual']:.6f} "
                f"val_low_intent={val_metrics['low_intent_action_mag']:.6f}"
            )

    ckpt = torch.load(best_pt, map_location=device, weights_only=False)
    model.load_state_dict(ckpt["model_state"])
    model.eval()

    # Save final checkpoint copy
    final_pt = out_dir / "residual_bc_final.pt"
    torch.save(
        {
            "model_state": model.state_dict(),
            "best_epoch": best_epoch,
            "best_score": best_val,
            "args": vars(args),
            "history": history,
        },
        final_pt,
    )

    # Export ONNX
    onnx_path = out_dir / "residual_bc.onnx"
    dummy = torch.randn(1, 17, device=device)
    torch.onnx.export(
        model,
        dummy,
        onnx_path.as_posix(),
        input_names=["obs"],
        output_names=["residual"],
        dynamic_axes={"obs": {0: "batch"}, "residual": {0: "batch"}},
        opset_version=13,
    )

    summary = {
        "dataset": str(args.dataset),
        "train_samples": int(train_obs.shape[0]),
        "val_samples": int(val_obs.shape[0]),
        "best_epoch": int(best_epoch),
        "best_score": float(best_val),
        "best_checkpoint": str(best_pt),
        "final_checkpoint": str(final_pt),
        "onnx": str(onnx_path),
    }

    (out_dir / "training_summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    (out_dir / "training_history.json").write_text(json.dumps(history, indent=2), encoding="utf-8")

    print(json.dumps(summary, indent=2))


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Train residual BC model from offline dataset.")
    p.add_argument(
        "--dataset",
        type=Path,
        default=Path("OfflineTraining/ResidualBC/data/exp1_adaptivefly_residual_dataset.npz"),
    )
    p.add_argument("--output-dir", type=Path, default=Path("OfflineTraining/ResidualBC/artifacts"))

    p.add_argument("--epochs", type=int, default=120)
    p.add_argument("--batch-size", type=int, default=1024)
    p.add_argument("--lr", type=float, default=1.5e-4)
    p.add_argument("--weight-decay", type=float, default=1.0e-5)
    p.add_argument("--hidden", type=int, default=256)

    p.add_argument("--residual-clip", type=float, default=0.6)
    p.add_argument("--low-intent-threshold", type=float, default=0.12)
    p.add_argument("--low-intent-weight", type=float, default=0.4)
    p.add_argument("--residual-l2-weight", type=float, default=1.0e-3)
    p.add_argument("--low-intent-metric-weight", type=float, default=0.5)

    p.add_argument("--device", choices=["auto", "cuda", "cpu"], default="auto")
    p.add_argument("--log-every", type=int, default=5)

    return p


def main() -> None:
    args = build_parser().parse_args()
    if not args.dataset.exists():
        raise FileNotFoundError(f"Dataset not found: {args.dataset}")
    train(args)


if __name__ == "__main__":
    main()
