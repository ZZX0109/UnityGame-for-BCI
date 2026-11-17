# -*- coding: utf-8 -*-
"""Shared helpers for the frozen Neuralife.VirtualHand JSONL v1 wire contract.

Contract (docs/IMPLEMENTATION_BRIEF.md + Assets/VirtualHand/Scripts/Core/HandMotionFrame.cs):
one UTF-8 JSON object per line with exactly the HandMotionFrame fields. Pose
values are 20 normalized [0,1] rig controls in finger-major order
(thumb, index, middle, ring, little); the thumb block is
CMC flexion, MCP flexion, IP flexion, opposition; every other finger block is
MCP, PIP, DIP, spread. This is visual rig control, not a claim of measured angles.
"""

import json
import math

INTENTS = ("rest", "left", "right", "both")
SOURCES = ("manual", "glove", "labels", "prediction")
N_JOINTS = 20

# Documented joint order (index 0..19).
JOINT_LABELS = [
    "thumb_cmc_flex", "thumb_mcp_flex", "thumb_ip_flex", "thumb_opposition",
    "index_mcp", "index_pip", "index_dip", "index_spread",
    "middle_mcp", "middle_pip", "middle_dip", "middle_spread",
    "ring_mcp", "ring_pip", "ring_dip", "ring_spread",
    "little_mcp", "little_pip", "little_dip", "little_spread",
]


class FrameValidationError(ValueError):
    """Raised for malformed, non-finite or out-of-contract frame input."""


def clamp01(x):
    """Clamp a float to [0, 1]."""
    return 0.0 if x < 0.0 else (1.0 if x > 1.0 else float(x))


def validate_frame(frame, where=""):
    """Validate one frame dict against the wire contract; return a clean copy.

    Raises FrameValidationError on malformed / non-finite / unknown values.
    `where` is prepended to error messages (e.g. "file.jsonl:42").
    """
    prefix = f"{where}: " if where else ""
    if not isinstance(frame, dict):
        raise FrameValidationError(prefix + "frame is not a JSON object")
    if frame.get("version") != 1:
        raise FrameValidationError(prefix + f"unsupported version {frame.get('version')!r}")

    def finite(name):
        v = frame.get(name)
        if isinstance(v, bool) or not isinstance(v, (int, float)):
            raise FrameValidationError(prefix + f"{name} is not a number: {v!r}")
        v = float(v)
        if not math.isfinite(v):
            raise FrameValidationError(prefix + f"{name} is not finite: {v!r}")
        return v

    time = finite("time")
    duration = finite("duration")
    if time < 0.0:
        raise FrameValidationError(prefix + f"time must be >= 0, got {time}")
    if duration <= 0.0:
        raise FrameValidationError(prefix + f"duration must be > 0, got {duration}")

    intent = frame.get("intent")
    if intent not in INTENTS:
        raise FrameValidationError(prefix + f"unknown intent {intent!r}")
    source = frame.get("source")
    if source not in SOURCES:
        raise FrameValidationError(prefix + f"unknown source {source!r}")

    confidence = finite("confidence")
    if confidence < 0.0 or confidence > 1.0:
        raise FrameValidationError(prefix + f"confidence must be in [0,1], got {confidence}")

    for key in ("subject", "run"):
        if not isinstance(frame.get(key), str):
            raise FrameValidationError(prefix + f"{key} must be a string")

    clean = {
        "version": 1,
        "time": time,
        "duration": duration,
        "intent": intent,
        "confidence": confidence,
        "source": source,
        "subject": frame["subject"],
        "run": frame["run"],
        "leftJoints": _validate_joints(frame.get("leftJoints"), prefix + "leftJoints"),
        "rightJoints": _validate_joints(frame.get("rightJoints"), prefix + "rightJoints"),
    }
    if clean["leftJoints"] is None:
        del clean["leftJoints"]
    if clean["rightJoints"] is None:
        del clean["rightJoints"]
    return clean


def _validate_joints(joints, prefix):
    if joints is None:
        return None
    if not isinstance(joints, (list, tuple)) or len(joints) != N_JOINTS:
        raise FrameValidationError(prefix + f" must have {N_JOINTS} values, got {joints!r}")
    out = []
    for i, v in enumerate(joints):
        if isinstance(v, bool) or not isinstance(v, (int, float)):
            raise FrameValidationError(prefix + f"[{i}] is not a number: {v!r}")
        v = float(v)
        if not math.isfinite(v):
            raise FrameValidationError(prefix + f"[{i}] is not finite: {v!r}")
        if v < 0.0 or v > 1.0:
            raise FrameValidationError(
                prefix + f"[{i}] ({JOINT_LABELS[i]}) outside [0,1]: {v}")
        out.append(v)
    return out


def write_jsonl(path, frames):
    """Write frames as one UTF-8 JSON object per line (validated)."""
    import os
    os.makedirs(os.path.dirname(os.path.abspath(path)) or ".", exist_ok=True)
    n = 0
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for fr in frames:
            clean = validate_frame(fr)
            f.write(json.dumps(clean, ensure_ascii=False, separators=(",", ":")))
            f.write("\n")
            n += 1
    return n


def write_replay(path, frames):
    """Write a replay file: per-frame contract validation plus the cross-frame
    guarantee that times are non-decreasing (single-frame checks alone cannot
    catch a stretched or shuffled timeline)."""
    clean = [validate_frame(fr) for fr in frames]
    for prev, nxt in zip(clean, clean[1:]):
        if nxt["time"] < prev["time"]:
            raise FrameValidationError(
                f"{path}: time {nxt['time']} goes backwards after {prev['time']}")
    import os
    os.makedirs(os.path.dirname(os.path.abspath(path)) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for fr in clean:
            f.write(json.dumps(fr, ensure_ascii=False, separators=(",", ":")))
            f.write("\n")
    return len(clean)


def iter_jsonl(path):
    """Yield (line_number, validated_frame). Raises visibly on bad input."""
    with open(path, "r", encoding="utf-8") as f:
        for lineno, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError as exc:
                raise FrameValidationError(f"{path}:{lineno}: malformed JSON ({exc})") from exc
            yield lineno, validate_frame(obj, where=f"{path}:{lineno}")


def read_jsonl(path):
    """Read and validate a whole JSONL file into a list."""
    return [fr for _, fr in iter_jsonl(path)]
