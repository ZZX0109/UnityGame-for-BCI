# -*- coding: utf-8 -*-
"""Small unit tests for the Game04 data tools (wire contract + decoders).

Run (from the project root):
    D:/SoftwareManagement/venv/bci/Scripts/python.exe -I Tools/test_tools.py
All tests are synthetic and fast; no dataset download required.
"""

import os
import sys
import tempfile

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import wire
from wire import FrameValidationError

FAILURES = []


def check(name, fn):
    try:
        fn()
        print(f"PASS {name}")
    except AssertionError as exc:
        print(f"FAIL {name}: {exc}")
        FAILURES.append(name)
    except Exception as exc:  # noqa: BLE001
        print(f"FAIL {name}: unexpected {type(exc).__name__}: {exc}")
        FAILURES.append(name)


def expect_raise(exc_type, fn):
    try:
        fn()
    except exc_type:
        return
    raise AssertionError(f"expected {exc_type.__name__}")


# ---------------------------------------------------------------- wire contract
def test_valid_frame_roundtrip():
    fr = {"version": 1, "time": 1.5, "duration": 2.0, "intent": "left",
          "confidence": 0.7, "source": "prediction", "subject": "S001",
          "run": "R12", "leftJoints": [0.5] * 20}
    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, "t.jsonl")
        wire.write_jsonl(p, [fr])
        out = wire.read_jsonl(p)
    assert out[0]["intent"] == "left"
    assert out[0]["time"] == 1.5
    assert len(out[0]["leftJoints"]) == 20


def test_rejects_unknown_intent_and_source():
    base = {"version": 1, "time": 0.0, "duration": 1.0, "intent": "rest",
            "confidence": 1.0, "source": "manual", "subject": "", "run": ""}
    expect_raise(FrameValidationError,
                 lambda: wire.validate_frame({**base, "intent": "fifth"}))
    expect_raise(FrameValidationError,
                 lambda: wire.validate_frame({**base, "source": "truth"}))


def test_rejects_bad_time_duration_confidence():
    base = {"version": 1, "time": 0.0, "duration": 1.0, "intent": "rest",
            "confidence": 1.0, "source": "manual", "subject": "", "run": ""}
    expect_raise(FrameValidationError,
                 lambda: wire.validate_frame({**base, "time": -1.0}))
    expect_raise(FrameValidationError,
                 lambda: wire.validate_frame({**base, "duration": 0.0}))
    expect_raise(FrameValidationError,
                 lambda: wire.validate_frame({**base, "confidence": 1.5}))
    expect_raise(FrameValidationError,
                 lambda: wire.validate_frame({**base, "time": float("nan")}))


def test_rejects_bad_joints():
    base = {"version": 1, "time": 0.0, "duration": 1.0, "intent": "rest",
            "confidence": 1.0, "source": "manual", "subject": "", "run": ""}
    expect_raise(FrameValidationError,
                 lambda: wire.validate_frame({**base, "leftJoints": [0.5] * 19}))
    expect_raise(FrameValidationError,
                 lambda: wire.validate_frame({**base, "leftJoints": [2.0] * 20}))
    expect_raise(FrameValidationError,
                 lambda: wire.validate_frame(
                     {**base, "leftJoints": [float("inf")] + [0.0] * 19}))


def test_malformed_jsonl_fails_visibly():
    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, "bad.jsonl")
        with open(p, "w", encoding="utf-8") as fh:
            fh.write('{"version": 1, "time": 0.0, "duration": 1.0}\n')
            fh.write("not json at all\n")
        expect_raise(FrameValidationError, lambda: wire.read_jsonl(p))


# ---------------------------------------------------------------- EEG pieces
def test_event_class_mapping_matches_physionet_record_descriptions():
    import eeg_decode as e

    # Unilateral runs (04/08/12): T1 left, T2 right
    assert e.event_class(4, "T1") == "left" and e.event_class(4, "T2") == "right"
    assert e.event_class(12, "T1") == "left"
    # Bilateral (06/10/14): T1 both fists, T2 both feet -> excluded (None)
    assert e.event_class(6, "T1") == "both"
    assert e.event_class(6, "T2") is None
    assert e.event_class(14, "T2") is None
    assert e.event_class(10, "T0") == "rest"
    expect_raise(ValueError, lambda: e.event_class(3, "T1"))


def test_event_class_rejects_out_of_contract_run_even_for_t0():
    # DHS finding 5: T0 must not bypass run-membership validation, otherwise
    # an out-of-contract run would silently contribute "rest" epochs.
    import eeg_decode as e

    for run in (3, 5, 7, 9, 11, 13, 1, 2):
        expect_raise(ValueError, lambda r=run: e.event_class(r, "T0"))


def test_combine_files_one_offset_per_file_regression():
    # DHS finding 1 regression: merged timeline must be last subject time +
    # one 2 s gap per boundary (~380 s for 3 subjects x 52 frames), NOT
    # ~12481 s from re-shifting every frame against the global clock.
    import eeg_decode as e
    import wire

    base = {"version": 1, "duration": 2.0, "intent": "rest", "confidence": 0.5,
            "source": "prediction", "subject": "SX", "run": "R12"}
    with tempfile.TemporaryDirectory() as d:
        for subject in ("S001", "S002", "S003"):
            frames = [{**base, "subject": subject,
                       "time": round(2.5 + k * 2.4, 3)} for k in range(52)]
            wire.write_jsonl(os.path.join(d, f"eeg_{subject}_predictions.jsonl"),
                             frames)
        out = e.combine_files(("S001", "S002", "S003"), d, "predictions",
                              "merged.jsonl")
        merged = wire.read_jsonl(out)
    assert len(merged) == 156
    # each subject spans 2.5..124.9 locally; offset accumulates per file
    local_last = 2.5 + 51 * 2.4
    expected_last = 3 * local_last + 2 * e.SUBJECT_GAP_S   # = 378.7
    assert abs(merged[-1]["time"] - expected_last) < 0.01, \
        f"merged last time {merged[-1]['time']} != expected {expected_last:.1f}"
    times = [f["time"] for f in merged]
    assert all(b >= a for a, b in zip(times, times[1:]))
    # within-subject spacing (2.4 s) and the subject-boundary gap survive
    gaps = {round(b - a, 6) for a, b in zip(times, times[1:])}
    assert gaps == {2.4, round(e.SUBJECT_GAP_S + 2.5, 6)}


def test_wire_write_replay_rejects_backwards_time():
    fr = lambda t: {"version": 1, "time": t, "duration": 1.0, "intent": "rest",  # noqa: E731
                    "confidence": 1.0, "source": "glove", "subject": "", "run": ""}
    with tempfile.TemporaryDirectory() as d:
        p = os.path.join(d, "r.jsonl")
        expect_raise(FrameValidationError,
                     lambda: wire.write_replay(p, [fr(5.0), fr(4.9)]))
        assert wire.write_replay(p, [fr(1.0), fr(1.0), fr(3.0)]) == 3  # equal OK


def test_db8_export_indices_no_drift():
    import db8_export as dbe

    # Non-divisor fps: 30 Hz -> step 67, real cadence 67/2000 s. Kept indices
    # are ORIGINAL sample indices; timestamps must be index/FS (native clock),
    # not index/fps, which would drift when fps does not divide 2000 exactly.
    step, keep = dbe.export_indices(6000, 30.0)
    assert step == 67
    assert keep[:4].tolist() == [0, 67, 134, 201]
    assert keep[10] / dbe.FS == 670 / 2000.0
    # Divisor fps unchanged: 40 Hz -> step 50
    step40, keep40 = dbe.export_indices(6000, 40.0)
    assert step40 == 50 and keep40[7] == 350
    assert keep40[7] / dbe.FS == 0.175


def test_no_train_test_leak_in_run_split():
    import eeg_decode as e

    seen = []
    for runs in e.SPLIT_RUNS.values():
        for r in runs:
            assert r not in seen, f"run {r} appears in two splits"
            seen.append(r)
    assert sorted(seen) == ["04", "06", "08", "10", "12", "14"]


def test_epoch_windowing_causal_and_classed():
    import eeg_decode as e

    sf = 160.0
    data = np.zeros((4, int(20 * sf)))          # 4 ch, 20 s of signal
    ann = [(1.0, "T1"), (4.0, "T0"), (7.0, "T2")]
    X, metas = e.build_epochs(data, sf, ann, run=4)   # unilateral run
    # epoch = [onset+0.5, onset+2.5] -> 320 samples; nothing before onset+0.5
    assert X.shape == (3, 4, 320)
    assert metas[0] == (1.0 + 2.5, "left")
    assert metas[1] == (4.0 + 2.5, "rest")
    assert metas[2] == (7.0 + 2.5, "right")
    # bilateral run: T2 (both feet) epoch is dropped
    Xb, mb = e.build_epochs(data, sf, ann, run=6)
    assert Xb.shape[0] == 2 and [c for _, c in mb] == ["both", "rest"]


def test_csp_lda_learns_separable_synthetics():
    import eeg_decode as e

    rng = np.random.default_rng(42)
    n_ch, n = 20, 320
    # Class-discriminative spatial topographies: class "left" modulates the
    # first channel group, class "right" the second (classic CSP setup).
    w_left = np.r_[np.ones(10), np.zeros(10)]
    w_right = np.r_[np.zeros(10), np.ones(10)]

    def trials(w, n_tr):
        out = np.zeros((n_tr, n_ch, n))
        for k in range(n_tr):
            src = np.sin(2 * np.pi * 10 * np.arange(n) / 160.0
                         + rng.normal(0, 0.3))          # random phase per trial
            out[k] = rng.normal(0, 0.1, (n_ch, n)) + np.outer(w, 3 * src)
        return out

    Xa, Xb = trials(w_left, 30), trials(w_right, 30)
    X = np.concatenate([Xa, Xb])
    y = ["left"] * 30 + ["right"] * 30
    Xtr, Xte, ytr, yte = X[:36], X[36:], y[:36], y[36:]
    csp, lda = e.fit_csp_lda(Xtr, ytr)
    pred, proba = e.predict(csp, lda, Xte)
    acc = float(np.mean([p == t for p, t in zip(pred, yte)]))
    assert acc > 0.9, f"CSP/LDA failed on separable synthetic: acc={acc:.2f}"


def test_metrics_helpers():
    import eeg_decode as e

    y_true = ["rest", "rest", "left", "left", "both", "both"]
    y_pred = ["rest", "left", "left", "left", "both", "rest"]
    # rest 1/2, left 2/2, both 1/2 (right absent everywhere -> skipped)
    assert abs(e.balanced_accuracy(y_true, y_pred) - (0.5 + 1.0 + 0.5) / 3) < 1e-9
    cm = e.confusion_fixed(y_true, y_pred)
    assert cm[0, 0] == 1 and cm[0, 1] == 1 and cm[1, 1] == 2 and cm[3, 0] == 1
    assert cm.sum() == len(y_true)
    assert e.coverage([0.5, 0.9, 0.6]) == 2 / 3
    assert e.coverage([]) == 0.0


# ---------------------------------------------------------------- DB8 mapping
def test_db8_controls_shape_bounds_and_coupling():
    import db8_export as dbe

    rng = np.random.default_rng(42)
    glove = rng.uniform(-70, 170, size=(1000, 18)).astype(np.float32)
    ctrl = dbe.build_controls(glove)
    assert ctrl.shape == (1000, 20)
    assert np.isfinite(ctrl).all()
    assert ctrl.min() >= 0.0 and ctrl.max() <= 1.0
    # DIP == PIP coupling for all four fingers (no DIP sensors on this model)
    for f in range(4):
        assert np.allclose(ctrl[:, (f + 1) * 4 + 2], ctrl[:, (f + 1) * 4 + 1])
    # thumb block order: CMC(0), MCP(1), IP(2), opposition(3)
    assert np.allclose(ctrl[:, 0], dbe.robust01(glove[:, 1]))
    assert np.allclose(ctrl[:, 3], dbe.robust01(glove[:, 0]))


def test_db8_clip_extraction_and_timing():
    import db8_export as dbe

    stim = np.zeros(4000, dtype=int)
    stim[1000:2000] = 3          # index clip: 0.5 s..1.0 s at 2000 Hz
    run = dbe.longest_runs(stim, 3)
    assert run[0] == 1000 and run[-1] == 1999
    assert len(dbe.longest_runs(stim, 7)) == 0
    # full-length clip timing never exceeds its wall-clock span
    stim2 = np.ones(int(dbe.FS * 4), dtype=int)
    run2 = dbe.longest_runs(stim2, 1)
    t_last = run2[-1] / dbe.FS - run2[0] / dbe.FS
    assert abs(t_last - (4.0 - 1.0 / dbe.FS)) < 1e-9


def test_db8_exported_clip_times_match_native_clock():
    # End-to-end regression (DHS finding 4 + follow-up): a 4 s synthetic clip
    # exported at a non-divisor fps must span ~4 s on the native 2000 Hz
    # clock, with decimation step and frame duration following the real
    # cadence; times must stay monotonic.
    import db8_export as dbe

    fps = 30.0
    step, keep = dbe.export_indices(int(dbe.FS * 4), fps)
    run = keep[keep < 2 * dbe.FS]           # 2 s span of kept original indices
    times = [i / dbe.FS for i in run]
    # last kept sample below the 2 s mark: span is (n_kept-1) native steps
    span = times[-1] - times[0]
    assert abs(span - (len(run) - 1) * step / dbe.FS) < 1e-9, f"span {span} drifts"
    assert abs(span - 2.0) < step / dbe.FS  # and stays within one step of 2 s
    dur = round(step / dbe.FS, 6)
    assert abs(dur - 1.0 / (dbe.FS / step)) < 1e-6


def main():
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            check(name, fn)
    print()
    if FAILURES:
        print(f"{len(FAILURES)} test(s) failed: {', '.join(FAILURES)}")
        sys.exit(1)
    print("All tests passed.")


if __name__ == "__main__":
    main()
