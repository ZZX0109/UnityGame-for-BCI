# Game04 VirtualHand — Data Tools (GLM task B)

Real-data pipeline for the virtual hand demo: EEG motor-imagery decoding
(PhysioNet eegmmidb) and CyberGlove kinematic replay (Ninapro DB8), both
exported as UTF-8 JSONL conforming to the frozen contract in
`Assets/VirtualHand/Scripts/Core/HandMotionFrame.cs` (wire v1: 20 normalized
rig controls per hand, finger-major, intent in rest|left|right|both).

Task ownership per `docs/IMPLEMENTATION_BRIEF.md`: this directory owns
**Tools**, **Assets/StreamingAssets/Replays** exports and **Results/data**
metrics. Task A (ArtSource/Unity) and Codex integration are separate.

## Environment

- Python: `D:/SoftwareManagement/venv/bci/Scripts/python.exe` (ready-made venv)
- Pinned dependencies: `Tools/requirements.txt`

## 1. EEG motor-imagery decoding (`eeg_decode.py`)

Per-subject 4-class (rest/left/right/both) offline decoding of imagined
movement. Runs 04/08/12 are unilateral (T1=left fist, T2=right fist), runs
06/10/14 bilateral (T1=both fists; T2=both feet **excluded**). Verified against
the official PhysioNet record descriptions. Fixed split train 04+06 / val
08+10 / held-out test 12+14; causal 8-30 Hz filter, common-average reference,
0.5-2.5 s epochs, CSP(6) + shrinkage LDA, seed 42, fitted on training data
only. Predictions are timestamped at the end of their analysis window. Truth
is exported to **separate** files; Unity prediction mode never reads it.

```bash
# reproducible command (from project root)
D:/SoftwareManagement/venv/bci/Scripts/python.exe -I Tools/eeg_decode.py \
    --data D:/SoftwareManagement/datasets/eegmmidb/1.0.0 \
    --subjects S001,S002,S003 \
    --outdir Assets/StreamingAssets/Replays/eeg \
    --metrics Results/data
```

Outputs (Assets/StreamingAssets/Replays/eeg):
`eeg_S00X_predictions.jsonl`, `eeg_S00X_truth.jsonl`,
`eeg_predictions_all.jsonl`, `eeg_truth_all.jsonl`;
metrics in `Results/data/eeg_metrics*.json` (balanced accuracy, fixed-order
confusion rest/left/right/both, coverage at confidence >= 0.6, and the list of
retained incorrect test predictions — never corrected with truth).

Dataset download (if moving machines; ~45 MB, 18 EDF files):

```bash
python Tools/download_data.py --dest D:/SoftwareManagement/datasets
# or manually, for S in S001 S002 S003 and R in 04 06 08 10 12 14:
#   https://physionet.org/files/eegmmidb/1.0.0/{S}/{S}R{R}.edf
```

## 2. DB8 glove replay (`db8_export.py`)

Reads `S1_E1_A3.mat` (18 calibrated CyberGlove channels, left hand), maps
channels to the 20 normalized rig controls, and exports the longest
representative segment of each requested stimulus at 40 Hz (30-60 Hz band):
rest(0), thumb(1), index(3), cylindrical(7), lateral(8), tripod(9) grips.
Labels locate clips only — they are never a regression target. The glove is
the measured LEFT hand; a right-hand mirror is opt-in and clearly marked.

```bash
D:/SoftwareManagement/venv/bci/Scripts/python.exe -I Tools/db8_export.py \
    --mat D:/SoftwareManagement/datasets/NinaproDB8/S1_E1_A3.mat \
    --outdir Assets/StreamingAssets/Replays/db8
```

Documented assumptions (also in `db8_provenance.json`): official DB8 clock is
2000 Hz (glove sampled at 100 Hz, upsampled synchronized); no DIP sensors on
this glove model (DIP coupled to PIP); thumb channels ch0-3 = opposition,
CMC, MCP, IP flexion (empirically confirmed via stimulus selectivity);
channel values are normalized 1-99 percentile min-max — **not degrees**.

## 3. Unit tests

```bash
D:/SoftwareManagement/venv/bci/Scripts/python.exe -I Tools/test_tools.py
```

Covers: wire-contract validation/rejection (malformed, non-finite, unknown
intent/source, 20-joint bounds), PhysioNet event->class mapping, no
train/test run overlap, causal epoch windowing, CSP+LDA sanity on a separable
synthetic, metric helpers, DB8 channel-map/DIP-coupling and clip timing.

## Credits

- PhysioNet eegmmidb 1.0.0 (CC-BY 4.0): Schalk et al., 2004.
- Ninapro DB8 (CC-BY-SA 4.0): Krasoulis et al., Front. Neurosci. 2021.
