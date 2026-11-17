#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Download the exact dataset subset used by the Game04 virtual hand demo.

- PhysioNet eegmmidb 1.0.0: subjects S001-S003, runs 04,06,08,10,12,14 (18 EDF)
- Ninapro DB8: S1_E1_A3.mat

Usage:
    python -I download_data.py --dest D:/SoftwareManagement/datasets
"""

import argparse
import os
import sys
import urllib.request

EEG_BASE = "https://physionet.org/files/eegmmidb/1.0.0"
DB8_BASE = "https://ninapro.hevs.ch/files/DB8"
SUBJECTS = ("S001", "S002", "S003")
RUNS = ("04", "06", "08", "10", "12", "14")


def fetch(url, dest, size_hint=None):
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        print(f"exists  {dest}")
        return
    tmp = dest + ".part"
    print(f"fetch   {url}")
    urllib.request.urlretrieve(url, tmp)
    os.replace(tmp, dest)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--dest", default=os.path.abspath(
        os.path.join(here, "..", "..", "..", "..", "SoftwareManagement", "datasets")))
    ap.add_argument("--eeg-only", action="store_true")
    ap.add_argument("--db8-only", action="store_true")
    args = ap.parse_args()

    if not args.db8_only:
        for s in SUBJECTS:
            for r in RUNS:
                fetch(f"{EEG_BASE}/{s}/{s}R{r}.edf",
                      os.path.join(args.dest, "eegmmidb", "1.0.0", s, f"{s}R{r}.edf"))
    if not args.eeg_only:
        fetch(f"{DB8_BASE}/S1_E1_A3.mat",
              os.path.join(args.dest, "NinaproDB8", "S1_E1_A3.mat"))
    print("done ->", os.path.abspath(args.dest))


if __name__ == "__main__":
    sys.exit(main())
