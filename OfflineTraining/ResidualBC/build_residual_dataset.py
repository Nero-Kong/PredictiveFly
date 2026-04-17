#!/usr/bin/env python
"""Build residual BC dataset from AdaptiveFly Exp1 CSV logs.

Input CSV schema (per frame):
Time,RigPosX,RigPosY,RigPosZ,RigRotX,RigRotY,RigRotZ,RigRotW,
CamLocalPosX,CamLocalPosY,CamLocalPosZ,CamLocalRotX,CamLocalRotY,CamLocalRotZ,CamLocalRotW

Output NPZ contains:
- train_obs / val_obs: [N,17]
- train_target / val_target: residual action labels [N,4]
- train_rule / val_rule: dynamic baseline action [N,4]
- train_real / val_real: reconstructed real action from rig trajectory [N,4]
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import re
from pathlib import Path
from typing import Dict, Iterable, List, Sequence, Tuple

import numpy as np


def clamp(value: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, value))


def safe_norm(value: float, max_abs: float) -> float:
    if max_abs <= 1e-8:
        return 0.0
    return clamp(value / max_abs, -1.0, 1.0)


def delta_angle_deg(a_deg: float, b_deg: float) -> float:
    d = (b_deg - a_deg + 180.0) % 360.0 - 180.0
    return d


def quat_forward(x: float, y: float, z: float, w: float) -> np.ndarray:
    # Rotate (0,0,1) by quaternion (x,y,z,w)
    fx = 2.0 * (x * z + w * y)
    fy = 2.0 * (y * z - w * x)
    fz = 1.0 - 2.0 * (x * x + y * y)
    v = np.array([fx, fy, fz], dtype=np.float64)
    n = np.linalg.norm(v)
    if n < 1e-8:
        return np.array([0.0, 0.0, 1.0], dtype=np.float64)
    return v / n


def quat_to_rotmat(x: float, y: float, z: float, w: float) -> np.ndarray:
    xx = x * x
    yy = y * y
    zz = z * z
    xy = x * y
    xz = x * z
    yz = y * z
    wx = w * x
    wy = w * y
    wz = w * z
    return np.array(
        [
            [1.0 - 2.0 * (yy + zz), 2.0 * (xy - wz), 2.0 * (xz + wy)],
            [2.0 * (xy + wz), 1.0 - 2.0 * (xx + zz), 2.0 * (yz - wx)],
            [2.0 * (xz - wy), 2.0 * (yz + wx), 1.0 - 2.0 * (xx + yy)],
        ],
        dtype=np.float64,
    )


def world_to_local(vec_world: np.ndarray, quat_xyzw: np.ndarray) -> np.ndarray:
    r = quat_to_rotmat(quat_xyzw[0], quat_xyzw[1], quat_xyzw[2], quat_xyzw[3])
    return r.T @ vec_world


def pitch_yaw_deg_from_forward(forward: np.ndarray) -> Tuple[float, float]:
    planar = math.sqrt(float(forward[0] ** 2 + forward[2] ** 2))
    pitch = math.degrees(math.atan2(float(forward[1]), planar))
    yaw = math.degrees(math.atan2(float(forward[0]), float(forward[2])))
    return pitch, yaw


def alpha_lowpass(cutoff_hz: float, dt: float) -> float:
    if cutoff_hz <= 0.0:
        return 1.0
    return 1.0 - math.exp(-2.0 * math.pi * cutoff_hz * dt)


def compute_planar_speed(offset_mag: float, dead_zone: float, max_offset: float, exponent: float, max_speed: float) -> float:
    if offset_mag <= dead_zone:
        return 0.0
    m = max(dead_zone + 1e-4, max_offset)
    normalized = clamp((offset_mag - dead_zone) / (m - dead_zone), 0.0, 1.0)
    curved = normalized ** max(0.1, exponent)
    return curved * max(0.0, max_speed)


def compute_planar_direction(planar_offset: np.ndarray, head_forward_local: np.ndarray, head_blend: float) -> np.ndarray:
    lean_norm = np.linalg.norm(planar_offset)
    if lean_norm > 1e-8:
        lean = planar_offset / lean_norm
    else:
        lean = np.zeros(2, dtype=np.float64)

    head_planar = np.array([head_forward_local[0], head_forward_local[2]], dtype=np.float64)
    head_norm = np.linalg.norm(head_planar)
    if head_norm > 1e-8:
        head_planar = head_planar / head_norm
    else:
        head_planar = np.zeros(2, dtype=np.float64)

    if np.linalg.norm(lean) < 1e-8 and np.linalg.norm(head_planar) < 1e-8:
        return np.zeros(2, dtype=np.float64)
    if np.linalg.norm(lean) < 1e-8:
        return head_planar
    if np.linalg.norm(head_planar) < 1e-8:
        return lean

    blended = (1.0 - head_blend) * lean + head_blend * head_planar
    bnorm = np.linalg.norm(blended)
    if bnorm < 1e-8:
        return np.zeros(2, dtype=np.float64)
    return blended / bnorm


def compute_dynamic_yaw_rate_rad(
    head_forward_local: np.ndarray,
    planar_command: np.ndarray,
    max_yaw_rate_deg: float,
    gain: float,
    th_min_deg: float,
    th_max_deg: float,
    speed_threshold: float,
    speed_scale: float,
) -> float:
    head_yaw = math.atan2(float(head_forward_local[0]), float(head_forward_local[2]))
    max_rate = max_yaw_rate_deg * math.pi / 180.0
    th_min = th_min_deg * math.pi / 180.0
    th_max = th_max_deg * math.pi / 180.0

    planar_speed_norm = min(1.0, float(np.linalg.norm(planar_command)))
    vd = planar_speed_norm * speed_scale
    delta_theta = (th_min - th_max) / (1.0 + math.exp(speed_threshold - vd)) + th_max
    lam = 1.0 / (1.0 + math.exp(-gain * (abs(head_yaw) - delta_theta)))
    scaled = max(0.0, 2.0 * lam - 1.0)
    yaw_rate = max_rate * scaled * math.copysign(1.0, head_yaw) if abs(head_yaw) > 1e-8 else 0.0
    return clamp(yaw_rate, -max_rate, max_rate)


def compute_dynamic_vertical_speed(
    head_forward_local: np.ndarray,
    max_vertical_speed: float,
    up_lambda: float,
    up_delta_deg: float,
    down_lambda: float,
    down_delta_deg: float,
) -> float:
    planar = math.sqrt(float(head_forward_local[0] ** 2 + head_forward_local[2] ** 2))
    head_pitch_deg = math.degrees(math.atan2(float(head_forward_local[1]), planar))

    if head_pitch_deg >= 0.0:
        lam = up_lambda
        delta = up_delta_deg
        sign = 1.0
    else:
        lam = down_lambda
        delta = down_delta_deg
        sign = -1.0

    logistic = 1.0 / (1.0 + math.exp(-lam * (head_pitch_deg - delta)))
    logistic = clamp(logistic, 0.0, 1.0)
    return sign * logistic * max(0.0, max_vertical_speed)


def parse_subject_id(filename: str) -> str:
    m = re.match(r"^(P\d+)_", filename, flags=re.IGNORECASE)
    return m.group(1).upper() if m else "UNKNOWN"


def load_csv_to_arrays(csv_path: Path) -> Dict[str, np.ndarray]:
    with csv_path.open("r", encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        if reader.fieldnames is None:
            raise ValueError(f"No header in {csv_path}")
        columns: Dict[str, List[float]] = {k: [] for k in reader.fieldnames}
        for row in reader:
            for k in columns.keys():
                columns[k].append(float(row[k]))
    return {k: np.asarray(v, dtype=np.float64) for k, v in columns.items()}


def process_file(data: Dict[str, np.ndarray], args: argparse.Namespace) -> Tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    t = data["Time"]
    n = t.shape[0]
    if n < 3:
        return (
            np.zeros((0, 17), dtype=np.float32),
            np.zeros((0, 4), dtype=np.float32),
            np.zeros((0, 4), dtype=np.float32),
            np.zeros((0, 4), dtype=np.float32),
        )

    rig_pos = np.stack([data["RigPosX"], data["RigPosY"], data["RigPosZ"]], axis=1)
    rig_q = np.stack([data["RigRotX"], data["RigRotY"], data["RigRotZ"], data["RigRotW"]], axis=1)
    cam_local_pos = np.stack([data["CamLocalPosX"], data["CamLocalPosY"], data["CamLocalPosZ"]], axis=1)
    cam_local_q = np.stack([data["CamLocalRotX"], data["CamLocalRotY"], data["CamLocalRotZ"], data["CamLocalRotW"]], axis=1)

    anchor_local_pos = cam_local_pos[0].copy()
    f0 = quat_forward(cam_local_q[0, 0], cam_local_q[0, 1], cam_local_q[0, 2], cam_local_q[0, 3])
    anchor_pitch_deg, anchor_yaw_deg = pitch_yaw_deg_from_forward(f0)

    core = np.zeros((n, 4), dtype=np.float64)
    rates = np.zeros((n, 4), dtype=np.float64)
    confidence = np.zeros((n,), dtype=np.float64)
    head_forward = np.zeros((n, 3), dtype=np.float64)

    has_filter = False
    filtered = np.zeros((4,), dtype=np.float64)
    prev_filtered = np.zeros((4,), dtype=np.float64)

    for i in range(n):
        hf = quat_forward(cam_local_q[i, 0], cam_local_q[i, 1], cam_local_q[i, 2], cam_local_q[i, 3])
        head_forward[i] = hf

        cur_pitch_deg, cur_yaw_deg = pitch_yaw_deg_from_forward(hf)
        raw = np.array(
            [
                cam_local_pos[i, 0] - anchor_local_pos[0],
                cam_local_pos[i, 2] - anchor_local_pos[2],
                delta_angle_deg(anchor_pitch_deg, cur_pitch_deg),
                delta_angle_deg(anchor_yaw_deg, cur_yaw_deg),
            ],
            dtype=np.float64,
        )

        if abs(raw[0]) < args.planar_noise_floor:
            raw[0] = 0.0
        if abs(raw[1]) < args.planar_noise_floor:
            raw[1] = 0.0
        if abs(raw[2]) < args.angular_noise_floor_deg:
            raw[2] = 0.0
        if abs(raw[3]) < args.angular_noise_floor_deg:
            raw[3] = 0.0

        if not has_filter:
            filtered = raw.copy()
            prev_filtered = raw.copy()
            has_filter = True
            r = np.zeros((4,), dtype=np.float64)
        else:
            dt = max(float(t[i] - t[i - 1]), 1e-4)
            a = alpha_lowpass(args.lowpass_cutoff_hz, dt)
            filtered = filtered + a * (raw - filtered)
            r = (filtered - prev_filtered) / dt
            prev_filtered = filtered.copy()

        core[i] = filtered
        rates[i] = r

        planar_conf = clamp(math.sqrt(float(filtered[0] ** 2 + filtered[1] ** 2)) / max(args.planar_conf_start, 1e-4), 0.0, 1.0)
        pitch_conf = clamp(abs(float(filtered[2])) / max(args.pitch_conf_start_deg, 0.1), 0.0, 1.0)
        yaw_conf = clamp(abs(float(filtered[3])) / max(args.yaw_conf_start_deg, 0.1), 0.0, 1.0)
        confidence[i] = max(planar_conf, pitch_conf, yaw_conf)

    core_norm = np.zeros_like(core)
    core_norm[:, 0] = np.clip(core[:, 0] / args.planar_offset_range, -1.0, 1.0)
    core_norm[:, 1] = np.clip(core[:, 1] / args.planar_offset_range, -1.0, 1.0)
    core_norm[:, 2] = np.clip(core[:, 2] / args.pitch_range_deg, -1.0, 1.0)
    core_norm[:, 3] = np.clip(core[:, 3] / args.yaw_range_deg, -1.0, 1.0)

    rates_norm = np.zeros_like(rates)
    rates_norm[:, 0] = np.clip(rates[:, 0] / args.planar_rate_range, -1.0, 1.0)
    rates_norm[:, 1] = np.clip(rates[:, 1] / args.planar_rate_range, -1.0, 1.0)
    rates_norm[:, 2] = np.clip(rates[:, 2] / args.angular_rate_range_deg, -1.0, 1.0)
    rates_norm[:, 3] = np.clip(rates[:, 3] / args.angular_rate_range_deg, -1.0, 1.0)

    real_actions: List[np.ndarray] = []
    rule_actions: List[np.ndarray] = []
    residuals: List[np.ndarray] = []
    observations: List[np.ndarray] = []

    smoothed_planar_vel = np.zeros((2,), dtype=np.float64)

    for k in range(1, n):
        dt = float(t[k] - t[k - 1])
        if dt <= 1e-4:
            continue

        s = k - 1

        # Dynamic rule baseline from head-offset dynamic mapping.
        planar_offset = np.array([core[s, 0], core[s, 1]], dtype=np.float64)
        planar_speed = compute_planar_speed(
            float(np.linalg.norm(planar_offset)),
            args.planar_dead_zone,
            args.planar_max_offset,
            args.planar_response_exp,
            args.max_planar_speed,
        )
        planar_dir = compute_planar_direction(planar_offset, head_forward[s], args.head_direction_blend)
        desired_planar_vel = planar_dir * planar_speed
        planar_lerp = 1.0 - math.exp(-max(0.0, args.planar_smoothing) * dt)
        smoothed_planar_vel = smoothed_planar_vel + planar_lerp * (desired_planar_vel - smoothed_planar_vel)
        planar_cmd = smoothed_planar_vel / args.max_planar_speed if args.max_planar_speed > 1e-6 else np.zeros((2,), dtype=np.float64)

        yaw_rate_rad = compute_dynamic_yaw_rate_rad(
            head_forward[s],
            planar_cmd,
            args.max_yaw_rate_deg,
            args.dynamic_yaw_gain,
            args.dynamic_yaw_th_min_deg,
            args.dynamic_yaw_th_max_deg,
            args.dynamic_yaw_speed_threshold,
            args.dynamic_yaw_speed_scale,
        )
        vertical_speed = compute_dynamic_vertical_speed(
            head_forward[s],
            args.max_vertical_speed,
            args.dynamic_pitch_up_lambda,
            args.dynamic_pitch_up_delta_deg,
            args.dynamic_pitch_down_lambda,
            args.dynamic_pitch_down_delta_deg,
        )

        rule = np.array(
            [
                safe_norm(float(smoothed_planar_vel[0]), args.max_planar_speed),
                safe_norm(float(smoothed_planar_vel[1]), args.max_planar_speed),
                safe_norm(float(vertical_speed), args.max_vertical_speed),
                safe_norm(math.degrees(yaw_rate_rad), args.max_yaw_rate_deg),
            ],
            dtype=np.float64,
        )

        # Reconstruct realized action from rig motion.
        world_delta = rig_pos[k] - rig_pos[k - 1]
        local_delta = world_to_local(world_delta, rig_q[k - 1])
        local_vel = local_delta / dt

        yaw_prev = pitch_yaw_deg_from_forward(
            quat_forward(rig_q[k - 1, 0], rig_q[k - 1, 1], rig_q[k - 1, 2], rig_q[k - 1, 3])
        )[1]
        yaw_curr = pitch_yaw_deg_from_forward(
            quat_forward(rig_q[k, 0], rig_q[k, 1], rig_q[k, 2], rig_q[k, 3])
        )[1]
        yaw_rate_deg = delta_angle_deg(yaw_prev, yaw_curr) / dt

        real = np.array(
            [
                safe_norm(float(local_vel[0]), args.max_planar_speed),
                safe_norm(float(local_vel[2]), args.max_planar_speed),
                safe_norm(float(local_vel[1]), args.max_vertical_speed),
                safe_norm(float(yaw_rate_deg), args.max_yaw_rate_deg),
            ],
            dtype=np.float64,
        )

        residual = np.clip(real - rule, -args.residual_clip, args.residual_clip)

        prev_action = real_actions[-1] if real_actions else np.zeros((4,), dtype=np.float64)
        prev_velocity = prev_action

        obs = np.concatenate(
            [
                core_norm[s],
                rates_norm[s],
                prev_action,
                prev_velocity,
                np.array([confidence[s]], dtype=np.float64),
            ],
            axis=0,
        )

        observations.append(obs.astype(np.float32))
        real_actions.append(real.astype(np.float32))
        rule_actions.append(rule.astype(np.float32))
        residuals.append(residual.astype(np.float32))

    if not observations:
        return (
            np.zeros((0, 17), dtype=np.float32),
            np.zeros((0, 4), dtype=np.float32),
            np.zeros((0, 4), dtype=np.float32),
            np.zeros((0, 4), dtype=np.float32),
        )

    return (
        np.vstack(observations),
        np.vstack(residuals),
        np.vstack(rule_actions),
        np.vstack(real_actions),
    )


def stack_or_empty(chunks: Sequence[np.ndarray], width: int) -> np.ndarray:
    if not chunks:
        return np.zeros((0, width), dtype=np.float32)
    return np.vstack(chunks).astype(np.float32)


def parse_subject_list(raw: str) -> List[str]:
    if not raw.strip():
        return []
    return [x.strip().upper() for x in raw.split(",") if x.strip()]


def build_argparser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Build residual BC dataset from Exp1 CSV logs.")
    p.add_argument(
        "--data-dir",
        type=Path,
        default=Path(r"C:\Users\54402\OneDrive\Desktop\AdaptiveFly\Data\Exp1"),
        help="Directory containing Exp1 csv files.",
    )
    p.add_argument("--mode-keyword", type=str, default="AdaptiveFly", help="Only use files whose names contain this keyword.")
    p.add_argument("--output", type=Path, default=Path("OfflineTraining/ResidualBC/data/exp1_adaptivefly_residual_dataset.npz"))
    p.add_argument("--summary-json", type=Path, default=Path("OfflineTraining/ResidualBC/data/exp1_adaptivefly_residual_summary.json"))
    p.add_argument("--val-subjects", type=str, default="", help="Comma-separated subject IDs, e.g. P011,P012")
    p.add_argument("--residual-clip", type=float, default=0.6)

    # Feature extractor parameters (aligned with HeadIntentFeatureExtractor/AdaptiveFlyAgent defaults).
    p.add_argument("--lowpass-cutoff-hz", type=float, default=8.0)
    p.add_argument("--planar-noise-floor", type=float, default=0.002)
    p.add_argument("--angular-noise-floor-deg", type=float, default=0.2)

    p.add_argument("--planar-offset-range", type=float, default=0.35)
    p.add_argument("--pitch-range-deg", type=float, default=45.0)
    p.add_argument("--yaw-range-deg", type=float, default=90.0)
    p.add_argument("--planar-rate-range", type=float, default=2.0)
    p.add_argument("--angular-rate-range-deg", type=float, default=240.0)

    p.add_argument("--planar-conf-start", type=float, default=0.03)
    p.add_argument("--pitch-conf-start-deg", type=float, default=4.0)
    p.add_argument("--yaw-conf-start-deg", type=float, default=6.0)

    # Dynamic baseline parameters (aligned with HeadOffsetLocomotion dynamic defaults + current AdaptiveFly limits).
    p.add_argument("--max-planar-speed", type=float, default=6.0)
    p.add_argument("--max-vertical-speed", type=float, default=4.0)
    p.add_argument("--max-yaw-rate-deg", type=float, default=120.0)

    p.add_argument("--planar-dead-zone", type=float, default=0.04)
    p.add_argument("--planar-max-offset", type=float, default=0.25)
    p.add_argument("--planar-response-exp", type=float, default=1.8)
    p.add_argument("--head-direction-blend", type=float, default=0.55)
    p.add_argument("--planar-smoothing", type=float, default=10.0)

    p.add_argument("--dynamic-yaw-gain", type=float, default=0.5)
    p.add_argument("--dynamic-yaw-th-min-deg", type=float, default=12.0)
    p.add_argument("--dynamic-yaw-th-max-deg", type=float, default=30.0)
    p.add_argument("--dynamic-yaw-speed-threshold", type=float, default=7.0)
    p.add_argument("--dynamic-yaw-speed-scale", type=float, default=10.0)

    p.add_argument("--dynamic-pitch-up-lambda", type=float, default=0.3)
    p.add_argument("--dynamic-pitch-up-delta-deg", type=float, default=30.0)
    p.add_argument("--dynamic-pitch-down-lambda", type=float, default=-0.4)
    p.add_argument("--dynamic-pitch-down-delta-deg", type=float, default=-18.0)

    return p


def main() -> None:
    args = build_argparser().parse_args()
    data_dir = args.data_dir

    if not data_dir.exists() or not data_dir.is_dir():
        raise FileNotFoundError(f"Data dir not found: {data_dir}")

    csv_files = sorted(
        [
            p
            for p in data_dir.glob("*.csv")
            if args.mode_keyword.lower() in p.name.lower()
        ]
    )
    if not csv_files:
        raise RuntimeError(f"No CSV matched mode '{args.mode_keyword}' under {data_dir}")

    by_subject: Dict[str, Dict[str, List[np.ndarray]]] = {}

    for csv_path in csv_files:
        subject = parse_subject_id(csv_path.name)
        arrays = load_csv_to_arrays(csv_path)
        obs, target, rule, real = process_file(arrays, args)
        if obs.shape[0] == 0:
            continue

        if subject not in by_subject:
            by_subject[subject] = {
                "obs": [],
                "target": [],
                "rule": [],
                "real": [],
            }

        by_subject[subject]["obs"].append(obs)
        by_subject[subject]["target"].append(target)
        by_subject[subject]["rule"].append(rule)
        by_subject[subject]["real"].append(real)

    subjects = sorted(by_subject.keys())
    if not subjects:
        raise RuntimeError("No usable samples after processing.")

    val_subjects = parse_subject_list(args.val_subjects)
    if not val_subjects:
        # Default leave-last-2-subjects-out.
        val_subjects = subjects[-2:] if len(subjects) >= 2 else subjects[-1:]

    train_subjects = [s for s in subjects if s not in val_subjects]
    if not train_subjects:
        raise RuntimeError("Train split is empty. Adjust --val-subjects.")

    train_obs_chunks: List[np.ndarray] = []
    train_target_chunks: List[np.ndarray] = []
    train_rule_chunks: List[np.ndarray] = []
    train_real_chunks: List[np.ndarray] = []

    val_obs_chunks: List[np.ndarray] = []
    val_target_chunks: List[np.ndarray] = []
    val_rule_chunks: List[np.ndarray] = []
    val_real_chunks: List[np.ndarray] = []

    for s, payload in by_subject.items():
        obs = stack_or_empty(payload["obs"], 17)
        target = stack_or_empty(payload["target"], 4)
        rule = stack_or_empty(payload["rule"], 4)
        real = stack_or_empty(payload["real"], 4)

        if s in val_subjects:
            val_obs_chunks.append(obs)
            val_target_chunks.append(target)
            val_rule_chunks.append(rule)
            val_real_chunks.append(real)
        else:
            train_obs_chunks.append(obs)
            train_target_chunks.append(target)
            train_rule_chunks.append(rule)
            train_real_chunks.append(real)

    train_obs = stack_or_empty(train_obs_chunks, 17)
    train_target = stack_or_empty(train_target_chunks, 4)
    train_rule = stack_or_empty(train_rule_chunks, 4)
    train_real = stack_or_empty(train_real_chunks, 4)

    val_obs = stack_or_empty(val_obs_chunks, 17)
    val_target = stack_or_empty(val_target_chunks, 4)
    val_rule = stack_or_empty(val_rule_chunks, 4)
    val_real = stack_or_empty(val_real_chunks, 4)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(
        args.output,
        train_obs=train_obs,
        train_target=train_target,
        train_rule=train_rule,
        train_real=train_real,
        val_obs=val_obs,
        val_target=val_target,
        val_rule=val_rule,
        val_real=val_real,
    )

    summary = {
        "data_dir": str(data_dir),
        "mode_keyword": args.mode_keyword,
        "files_used": [p.name for p in csv_files],
        "subjects": subjects,
        "train_subjects": train_subjects,
        "val_subjects": val_subjects,
        "train_samples": int(train_obs.shape[0]),
        "val_samples": int(val_obs.shape[0]),
        "residual_clip": float(args.residual_clip),
        "output_npz": str(args.output),
    }

    args.summary_json.parent.mkdir(parents=True, exist_ok=True)
    args.summary_json.write_text(json.dumps(summary, indent=2), encoding="utf-8")

    print(json.dumps(summary, indent=2))


if __name__ == "__main__":
    main()
