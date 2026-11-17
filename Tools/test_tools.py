# -*- coding: utf-8 -*-
"""Small unit tests for the Game04 data tools (wire contract + decoders).

Run:
    python -I test_tools.py          (from Tools/, or set cwd anywhere)
All tests use synthetic data except one optional EDF smoke check skipped when
the dataset directory is absent.
"""

import json
import math
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
            fh.write('{"version": 1, "time": 0}\n')   # valid-ish but incomplete
            fh.write("not json at all\n")
        expect_raise(FrameValidationError, lambda: wire.read_jsonl(p))


# ---------------------------------------------------------------- EEG pieces
def test_event_class_mapping_matches_physionet_record_descriptions():
    import eeg_decode as e

    # Unilateral runs: T1 left, T2 right (runs 3,4,7,8,11,12)
    assert e.event_class(4, "T1") == "left" and e.event_class(4, "T2") == "right"
    assert e.event_class(12, "T1") == "left"
    # Bilateral: T1 both fists, T2 both feet -> excluded (None)
    assert e.event_class(6, "T1") == "both"
    assert e.event_class(6, "T2") is None
    assert e.event_class(14, "T2") is None
    assert e.event_class(10, "T0") == "rest"
    # Unknown run rejected loudly
    expect_raise(ValueError, lambda: e.event_class(3, "T1"))


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
    data = np.zeros((4, int(20 * sf)))          # 4 ch, 20 s
    ann = [(1.0, "T1"), (4.0, "T0"), (7.0, "T2")]
    X, metas = e.build_epochs(data, sf, ann, run=4)   # unilateral run
    # epoch = [onset+0.5, onset+2.5] -> 320 samples at 160 Hz, first sample
    # strictly AFTER the event (causal window, no pre-onset leakage)
    assert X.shape == (3, 4, 320)
    assert metas[0] == (1.0 + 2.5, "left")
    assert metas[1] == (4.0 + 2.5, "rest")
    assert metas[2] == (7.0 + 2.5, "right")
    # feet exclusion in bilateral run: T2 dropped
    Xb, mb = e.build_epochs(data, sf, ann, run=6)
    assert Xb.shape[0] == 2 and [c for _, c in mb] == ["left" if False else "both", "rest"]


def test_csp_lda_learns_separable_synthetics():
    import eeg_decode as e

    rng = np.random.default_rng(42)
    sf, n = 160.0, 320
    t = np.arange(n) / sf

    def trials(freq, amp, n_tr):
        return np.array([amp * np.sin(2 * np.pi * freq * t + rng.normal(0, 0.3))
                         + rng.normal(0, 0.1, n) for _ in range(n_tr)])[:, None, :]

    X_a = trials(10.0, 1.0, 30)   # mu-band burst on one channel
    X_b = trials(20.0, 1.0, 30)
    X = np.concatenate([X_a, X_b]).transpose(0, 2, 1)  # -> (n, 1, n_samp)
    X = np.repeat(X, 4, axis=1)                        # 4 identical channels
    y = ["left"] * 30 + ["right"] * 30
    # pad to 20 channels for CSP stability
    X = np.repeat(X, 5, axis=1)
    from sklearn.model_selection import train_test_split
    Xtr, Xte, ytr, yte = train_test_split(X, y, test_size=0.4, random_state=42,
                                          stratify=y)
    csp, lda = e.fit_csp_lda(Xtr, ytr)
    pred, proba = e.predict(csp, lda, Xte)
    acc = np.mean([p == t for p, t in zip(pred, yte)])
    assert acc > 0.9, f"CSP/LDA failed on separable synthetic: acc={acc:.2f}"


def test_metrics_helpers():
    import eeg_decode as e

    y_true = ["rest", "rest", "left", "left", "both", "both"]
    y_pred = ["rest", "left", "left", "left", "both", "rest"]
    ba = e.balanced_accuracy(y_true, y_pred)
    # rest 1/1, left 1/2, both 1/2 -> mean 0.8333
    assert abs(ba - 5.0 / 6.0) < 1e-9
    cm = e.confusion_fixed(y_true, y_pred)
    assert cm[0, 0] == 1 and cm[0, 1] == 1 and cm[1, 1] == 2 and cm[3, 0] == 1
    assert cm.sum() == len(y_true)
    assert 0.0 <= e.coverage_fixed(np.array([[0.5, 0.5], [0.9, 0.1]]), thr=0.6) == 0.5


# ---------------------------------------------------------------- DB8 mapping
def test_db8_controls_shape_and_bounds():
    sys.path.insert(0, HERE)
    import db8_export as dbe

    rng = np.random.default_rng(42)
    glove = rng.uniform(-70, 170, size=(1000, 18)).astype(np.float32)
    ctrl = dbe.build_controls(glove)
    assert ctrl.shape == (1000, 20)
    assert np.isfinite(ctrl).all()
    assert ctrl.min() >= 0.0 and ctrl.max() <= 1.0
    # DIP == PIP coupling: index DIP (col 6) must equal index PIP (col 5)
    assert np.allclose(ctrl[:, 6], ctrl[:, 5])
    # thumb block order: CMC(0), MCP(1), IP(2), opposition(3)
    assert np.allclose(ctrl[:, 0], dbe.robust01(glove[:, 1]))
    assert np.allclose(ctrl[:, 3], dbe.robust01(glove[:, 0]))


def test_db8_clip_extraction_and_timing():
    import db8_export as dbe

    stim = np.zeros(4000, dtype=int)
    stim[1000:2000] = 3          # index clip, 1 s at 2000 Hz
    run = dbe.longest_runs(stim, 3)
    assert run[0] == 1000 and run[-1] == 1999
    assert len(dbe.longest_runs(stim, 7)) == 0


def test_db8_times_never_exceed_clip_length():
    import db8_export as dbe

    # 4 s of synthetic data at FS, all labeled index
    stim = np.ones(int(dbe.FS * 4), dtype=int)
    run = dbe.longest_runs(stim, 1)
    fps = 40.0
    t_last = run[-1] / dbe.FS - run[0] / dbe.FS
    assert t_last == pytest.approx(4.0 - 1.0 / dbe.FS)


try:
    import pytest  # noqa: F401
    _HAS_PYTEST = True
except ImportError:
    _HAS_PYTEST = False

if _HAS_PYTEST:
    import pytest

    pytest.approx = pytest.approx if hasattr(pytest, "approx") else None


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
