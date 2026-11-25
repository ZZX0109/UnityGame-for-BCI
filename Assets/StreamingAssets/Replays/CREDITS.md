# Replay data credits and licences

This folder contains the JSONL replay exports used by the VirtualHand demo and replay tests. The exports were copied from the existing Game04 VirtualHand project; the original EDF and MAT source recordings are not included here.

## EEG Motor Movement/Imagery Dataset

The six subject-specific replay files, eeg_S001_predictions.jsonl, eeg_S001_truth.jsonl, eeg_S002_predictions.jsonl, eeg_S002_truth.jsonl, eeg_S003_predictions.jsonl and eeg_S003_truth.jsonl, are the existing PhysioNet replay exports. Their subject, run and source fields are preserved. The prediction files are offline decoder outputs; the truth files contain reference labels.

- Source dataset: [EEG Motor Movement/Imagery Dataset, version 1.0.0, PhysioNet](https://www.physionet.org/content/eegmmidb/1.0.0/), credited to Gerwin Schalk (2009).
- Dataset DOI: [10.13026/C28G6P](https://doi.org/10.13026/C28G6P).
- Dataset licence listed by PhysioNet: [Open Data Commons Attribution License v1.0](https://opendatacommons.org/licenses/by/1-0/).
- Dataset page requests citation of Schalk (2009) and the original BCI2000 paper: Schalk, G., McFarland, D. J., Hinterberger, T., Birbaumer, N. and Wolpaw, J. R. (2004), “BCI2000: A General-Purpose Brain-Computer Interface (BCI) System,” IEEE Transactions on Biomedical Engineering, 51(6), 1034–1043 ([PubMed PMID 15188875](https://pubmed.ncbi.nlm.nih.gov/15188875/)).

## NinaPro DB8 glove replays

The six db8_S1_*.jsonl clips and db8_provenance.json are the existing exports for subject S1, exercise E1, acquisition A3 (source file S1_E1_A3.mat). The provenance file records the channel mapping, timing, normalization and selected clips.

- Dataset: [NinaPro DB8 official instructions and subject/file table](https://ninapro.hevs.ch/instructions/DB8.html).
- Citation requested by the official DB8 page: Krasoulis, A., Vijayakumar, S. and Nazarpour, K. (2019), “Effect of User Practice on Prosthetic Finger Control With an Intuitive Myoelectric Decoder,” Frontiers in Neuroscience, 13:891, [doi:10.3389/fnins.2019.00891](https://doi.org/10.3389/fnins.2019.00891).
- The inherited donor manifest db8_provenance.json identifies the DB8 source as CC BY-SA 4.0; that recorded designation is preserved here and linked to the [CC BY-SA 4.0 deed](https://creativecommons.org/licenses/by-sa/4.0/). The official DB8 instructions page links the citation above but does not itself state a dataset licence, so the licence label here is explicitly attributed to the donor manifest.
