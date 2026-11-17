#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Game04 VirtualHand — PhysioNet EEGMMIDB offline decoder (GLM task B).

Contract: docs/IMPLEMENTATION_BRIEF.md "Actual EEG decoding" + frozen
Core/HandMotionFrame.cs. Data layout verified locally:

  runs 04/08/12  unilateral imagined:  T1 = left fist,  T2 = right fist
  runs 06/10/14  bilateral  imagined:  T1 = both fists, T2 = both feet (excluded)
  (Verified against PhysioNet record descriptions: "T1 indicates the left fist
   in runs 3,4,7,8,11,12 and both fists in runs 5,6,9,10,13,14".)

Pipeline per subject (seed 42, no test tuning):
  fixed split  train 04+06 | val 08+10 | held-out test 12+14
  causal 8-30 Hz Butterworth (forward-only, no filtfilt) -> common average
  reference on continuous data -> epochs 0.5-2.5 s after each event ->
  CSP(6) + shrinkage LDA fitted on train only.

Exports (Assets/StreamingAssets/Replays/eeg):
  eeg_<subject>_predictions.jsonl  source="prediction", honest errors kept
  eeg_<subject>_truth.jsonl        source="labels", separate reference file
  eeg_predictions_all.jsonl / eeg_truth_all.jsonl  combined subjects
Metrics (Results/data): eeg_metrics.json + eeg_metrics_<subject>.json

Dependencies: numpy, scipy, mne, scikit-learn (venv bci).
"""

import argparse
import json
import os
import sys
from collections import Counter

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wire  # noqa: E402

SEED = 42
FS_MIN, FS_MAX = 8.0, 30.0
EPOCH_T = (0.5, 2.5)          # seconds after event onset
CLASSES = ["rest", "left", "right", "both"]
CONF_THR = 0.6
FRAME_DURATION = EPOCH_T[1] - EPOCH_T[0]   # 2.0 s of analysis per prediction
UNI_RUNS = (4, 8, 12)         # T1 left, T2 right
BIL_RUNS = (6, 10, 14)        # T1 both fists, T2 both feet -> excluded
RUN_SPLIT = {4: "train", 6: "train", 8: "val", 10: "val", 12: "test", 14: "test"}
SPLIT_RUNS = {"train": ("04", "06"), "val": ("08", "10"), "test": ("12", "14")}
TEST_GAP_S = 1.0              # replay-timeline gap between the two test runs


def event_class(run, desc):
    """Map an eegmmidb annotation (T0/T1/T2) to an intent class.

    Returns None for events excluded from the 4-class problem (both feet).
    """
    if desc == "T0":
        return "rest"
    if run in UNI_RUNS:
        return {"T1": "left", "T2": "right"}[desc]
    if run in BIL_RUNS:
        return {"T1": "both", "T2": None}[desc]
    raise ValueError(f"run R{run:02d} is not part of the contracted run set")


def load_and_preprocess(edf_path):
    """Load 64-ch EEG; causal 8-30 Hz band-pass then common average reference."""
    import mne
    from scipy.signal import butter, sosfilt

    mne.set_log_level("ERROR")
    raw = mne.io.read_raw_edf(edf_path, preload=True, verbose="ERROR")
    sf = float(raw.info["sfreq"])
    eeg = mne.pick_types(raw.info, eeg=True, exclude=[])
    data = raw.get_data(picks=eeg)                       # volts, (n_ch, n)
    sos = butter(4, [FS_MIN, FS_MAX], btype="bandpass", fs=sf, output="sos")
    data = sosfilt(sos, data, axis=1)                    # forward-only: causal
    data = data - data.mean(axis=0, keepdims=True)       # CAR across channels
    ann = [(float(a["onset"]), str(a["description"])) for a in raw.annotations]
    return data, sf, ann


def build_epochs(data, sf, ann, run):
    """Non-overlapping 0.5-2.5 s epochs after each usable event.

    Returns (X (n, n_ch, n_samp), metas) with metas[i] = (window_end_s, class).
    """
    s0 = int(round(EPOCH_T[0] * sf))
    s1 = int(round(EPOCH_T[1] * sf))
    X, metas = [], []
    for onset, desc in sorted(ann):
        cls = event_class(run, desc)
        if cls is None:
            continue
        start = int(round(onset * sf)) + s0
        end = int(round(onset * sf)) + s1
        if start < 0 or end > data.shape[1]:
            continue
        X.append(data[:, start:end])
        metas.append((onset + EPOCH_T[1], cls))
    if not X:
        return np.zeros((0, data.shape[0], s1 - s0)), []
    return np.asarray(X, dtype=np.float64), metas


def fit_csp_lda(X, y):
    """CSP(6) + shrinkage LDA, fitted on training epochs only.

    Uniform priors over the classes present in training counter the strong
    rest over-representation of eegmmidb (15 rest events per run).
    """
    from mne.decoding import CSP
    from sklearn.discriminant_analysis import LinearDiscriminantAnalysis

    csp = CSP(n_components=6, reg="ledoit_wolf", log=True, norm_trace=False)
    csp.fit(X, y)
    classes = sorted(set(y))
    lda = LinearDiscriminantAnalysis(solver="lsqr", shrinkage="auto",
                                     priors=[1.0 / len(classes)] * len(classes))
    lda.fit(csp.transform(X), y)
    return csp, lda


def predict(csp, lda, X):
    P = csp.transform(X)
    return lda.predict(P), lda.predict_proba(P)


def balanced_accuracy(y_true, y_pred):
    """Macro recall over classes present in truth or prediction."""
    recalls = []
    for cls in CLASSES:
        if cls not in y_true and cls not in y_pred:
            continue
        tp = sum(1 for t, p in zip(y_true, y_pred) if t == cls and p == cls)
        total = sum(1 for t in y_true if t == cls)
        recalls.append(tp / total if total else 0.0)
    return float(np.mean(recalls)) if recalls else float("nan")


def coverage(confs, thr=CONF_THR):
    """Fraction of predictions with max class probability >= thr."""
    if len(confs) == 0:
        return 0.0
    return float(np.mean(np.asarray(confs) >= thr))


def confusion_fixed(y_true, y_pred):
    idx = {c: i for i, c in enumerate(CLASSES)}
    m = np.zeros((len(CLASSES), len(CLASSES)), dtype=int)
    for t, p in zip(y_true, y_pred):
        m[idx[t], idx[p]] += 1
    return m


def run_subject(subject, data_root, outdir):
    """Train/evaluate one subject; write prediction+truth JSONL; return metrics."""
    splits = {s: [] for s in SPLIT_RUNS}
    for split, runs in SPLIT_RUNS.items():
        for run in runs:
            path = os.path.join(data_root, subject, f"{subject}R{run}.edf")
            data, sf, ann = load_and_preprocess(path)
            X, metas = build_epochs(data, sf, ann, int(run))
            splits[split].append({"run": run, "X": X, "metas": metas})

    # No-leak guard: split run sets are disjoint by construction.
    all_runs = [r for ss in SPLIT_RUNS.values() for r in ss]
    assert len(all_runs) == len(set(all_runs)), "run split overlap would leak"

    X_tr = np.concatenate([b["X"] for b in splits["train"] if len(b["X"])])
    y_tr = [c for b in splits["train"] for _, c in b["metas"]]
    csp, lda = fit_csp_lda(X_tr, y_tr)

    metrics = {"subject": subject, "seed": SEED,
               "filter": f"causal butterworth ord4 {FS_MIN:.0f}-{FS_MAX:.0f} Hz + CAR",
               "epoch_s": list(EPOCH_T), "csp_components": 6,
               "classifier": "shrinkage LDA (lsqr, auto shrinkage, uniform priors)",
               "train_counts": dict(Counter(y_tr))}
    test_records = []  # (run, window_end, truth_cls, pred_idx, confidence)
    for split in ("val", "test"):
        y_true, y_pred, confs, recs = [], [], [], []
        for b in splits[split]:
            if not len(b["X"]):
                continue
            pred, proba = predict(csp, lda, b["X"])
            truth = [c for _, c in b["metas"]]
            y_true.extend(truth)
            y_pred.extend(str(p) for p in pred)
            confs.extend(proba.max(axis=1))
            recs.extend((b["run"], w, c, str(p), float(cf))
                        for (w, c), p, cf in zip(b["metas"], pred, proba.max(axis=1)))
        cm = confusion_fixed(y_true, y_pred)
        metrics[f"{split}_balanced_accuracy"] = round(balanced_accuracy(y_true, y_pred), 4)
        metrics[f"{split}_confusion_rest_left_right_both"] = cm.tolist()
        metrics[f"{split}_coverage_at_{CONF_THR}"] = round(coverage(confs), 4)
        metrics[f"{split}_n"] = len(y_true)
        if split == "test":
            test_records = recs
    # Incorrect predictions retained verbatim (never corrected with truth).
    metrics["incorrect_test_predictions"] = [
        {"run": f"R{r}", "time_s": round(w, 3), "truth": c,
         "predicted": p, "confidence": round(cf, 4)}
        for r, w, c, p, cf in test_records if p != c]

    # --- export: prediction and truth in separate files, shared timeline ---
    pred_frames, truth_frames = [], []
    t_shift = 0.0
    for run in SPLIT_RUNS["test"]:
        frames = [f for f in test_records if f[0] == run]
        for r, w, truth, p, cf in frames:
            base = {"version": 1, "time": round(t_shift + w, 3),
                    "duration": FRAME_DURATION, "subject": subject,
                    "run": f"R{r}"}
            pred_frames.append({**base, "intent": p,
                                "confidence": round(cf, 4), "source": "prediction"})
            truth_frames.append({**base, "intent": truth, "confidence": 1.0,
                                 "source": "labels"})
        run_last = max((w for r, w, _, _, _ in test_records if r == run),
                       default=0.0)
        t_shift += run_last + TEST_GAP_S

    os.makedirs(outdir, exist_ok=True)
    pred_path = os.path.join(outdir, f"eeg_{subject}_predictions.jsonl")
    truth_path = os.path.join(outdir, f"eeg_{subject}_truth.jsonl")
    wire.write_jsonl(pred_path, pred_frames)
    wire.write_jsonl(truth_path, truth_frames)
    metrics["prediction_file"] = os.path.relpath(pred_path)
    metrics["truth_file"] = os.path.relpath(truth_path)
    return metrics


def combine_files(subjects, outdir, suffix, out_name):
    """Concatenate per-subject files onto one monotonic replay timeline."""
    frames, t_shift, last_t = [], 0.0, 0.0
    for subject in subjects:
        path = os.path.join(outdir, f"eeg_{subject}_{suffix}.jsonl")
        for fr in wire.read_jsonl(path):
            if fr["time"] < last_t - 1e-9:   # new subject block begins
                t_shift = last_t + 2.0
            last_t = t_shift + fr["time"]
            frames.append({**fr, "time": round(last_t, 3)})
    path = os.path.join(outdir, out_name)
    wire.write_jsonl(path, frames)
    return path


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="EEGMMIDB offline decoder -> JSONL replay")
    ap.add_argument("--data", default="D:/SoftwareManagement/datasets/eegmmidb/1.0.0",
                    help="directory containing S001/ S002/ S003/")
    ap.add_argument("--subjects", default="S001,S002,S003")
    ap.add_argument("--outdir",
                    default=os.path.join(here, "..", "Assets", "StreamingAssets",
                                         "Replays", "eeg"))
    ap.add_argument("--metrics", default=os.path.join(here, "..", "Results", "data"))
    args = ap.parse_args()

    os.makedirs(args.outdir, exist_ok=True)
    os.makedirs(args.metrics, exist_ok=True)
    subjects = args.subjects.split(",")
    all_metrics = {}
    for subject in subjects:
        m = run_subject(subject, args.data, args.outdir)
        all_metrics[subject] = m
        print(f"[{subject}] val BA {m['val_balanced_accuracy']:.3f} | "
              f"test BA {m['test_balanced_accuracy']:.3f} | "
              f"coverage@0.6 {m['test_coverage_at_0.6']:.3f} | "
              f"test n {m['test_n']} | errors kept "
              f"{len(m['incorrect_test_predictions'])}")
        with open(os.path.join(args.metrics, f"eeg_metrics_{subject}.json"),
                  "w", encoding="utf-8") as fh:
            json.dump(m, fh, ensure_ascii=False, indent=2)

    p_all = combine_files(subjects, args.outdir, "predictions",
                          "eeg_predictions_all.jsonl")
    t_all = combine_files(subjects, args.outdir, "truth", "eeg_truth_all.jsonl")
    summary = {"seed": SEED, "subjects": subjects,
               "mean_test_balanced_accuracy": round(float(np.mean(
                   [m["test_balanced_accuracy"] for m in all_metrics.values()])), 4),
               "per_subject": all_metrics,
               "combined_prediction_file": os.path.relpath(p_all),
               "combined_truth_file": os.path.relpath(t_all)}
    with open(os.path.join(args.metrics, "eeg_metrics.json"), "w",
              encoding="utf-8") as fh:
        json.dump(summary, fh, ensure_ascii=False, indent=2)
    print(f"Mean test balanced accuracy: {summary['mean_test_balanced_accuracy']:.3f}")
    print("wrote", p_all)


if __name__ == "__main__":
    main()
