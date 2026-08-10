from __future__ import annotations

import argparse
import json
from pathlib import Path

import librosa
import numpy as np

from maicaiyin.features import extract_features
from maicaiyin.numpy_model import JointPlacementModel


CHUNK_TICKS = 384


def parse_level(value: str) -> float:
    level = float(value[:-1]) + 0.75 if value.endswith("+") else float(value)
    if not 1 <= level <= 15:
        raise argparse.ArgumentTypeError("level must be between 1 and 15")
    return level


def estimate_grid(audio, sample_rate, bpm, offset):
    if bpm is not None and offset is not None:
        return float(bpm), float(offset)
    beat_kwargs = {"bpm": float(bpm)} if bpm is not None else {}
    tempo, beat_frames = librosa.beat.beat_track(
        y=audio, sr=sample_rate, **beat_kwargs
    )
    estimated_bpm = (
        float(bpm)
        if bpm is not None
        else float(np.asarray(tempo).reshape(-1)[0])
    )
    beat_times = librosa.frames_to_time(beat_frames, sr=sample_rate)
    estimated_offset = float(beat_times[0]) if len(beat_times) else 0.0
    return estimated_bpm, float(offset) if offset is not None else estimated_offset


def render_maidata(onset_ticks, bpm, offset, title, level, length):
    selected = set(onset_ticks)
    note_index = 0
    slices = []
    for tick in range(length):
        payload = ""
        if tick in selected:
            payload = str(note_index % 8 + 1)
            note_index += 1
        slices.append((f"({bpm:g}){{96}}" if tick == 0 else "") + payload)
    return "\n".join(
        (
            f"&title={title} — Maicaiyin",
            "&artist=generated onset skeleton",
            f"&first={offset:g}",
            "&des_1=Maicaiyin onset model",
            f"&lv_1={level:g}",
            "&inote_1=",
            ",".join(slices) + ",E",
            "",
        )
    )


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate a maimai onset-only maidata chart from audio."
    )
    parser.add_argument("audio", type=Path)
    parser.add_argument("--output", type=Path, default=Path("output"))
    parser.add_argument("--level", type=parse_level, default=13.0)
    parser.add_argument("--bpm", type=float)
    parser.add_argument("--offset", type=float)
    parser.add_argument("--title")
    parser.add_argument(
        "--threshold",
        type=float,
        default=0.550000011920929,
        help="tick probability threshold; lower values produce denser charts",
    )
    parser.add_argument(
        "--model",
        type=Path,
        default=Path(__file__).resolve().parent / "joint-placement-numpy.npz",
    )
    args = parser.parse_args()

    numpy_version = tuple(int(part) for part in np.__version__.split(".")[:2])
    if numpy_version != (2, 3):
        parser.error(
            f"this packaged engine requires NumPy 2.3.x, found {np.__version__}"
        )
    if args.bpm is not None and args.bpm <= 0:
        parser.error("--bpm must be positive")
    if not 0 < args.threshold < 1:
        parser.error("--threshold must be between 0 and 1")
    if not args.model.is_file():
        parser.error(f"model not found: {args.model}")

    audio, sample_rate = librosa.load(args.audio, sr=None, mono=True)
    bpm, offset = estimate_grid(audio, sample_rate, args.bpm, args.offset)
    if not np.isfinite(bpm) or bpm <= 0:
        parser.error("BPM could not be estimated; enter BPM manually")
    duration = len(audio) / sample_rate
    tick_seconds = 60 / (bpm * 24)
    times = np.arange(offset, duration, tick_seconds, dtype=np.float64)
    if not len(times):
        parser.error("audio is shorter than the selected offset")
    print(f"Extracting {len(times)} beat-grid frames at {bpm:.3f} BPM...", flush=True)
    features = extract_features(audio, times, sample_rate)

    model = JointPlacementModel(args.model)
    level_condition = np.asarray([(args.level - 12) / 3], dtype=np.float32)
    chunks = []
    for start in range(0, len(features), CHUNK_TICKS):
        part = features[start : start + CHUNK_TICKS]
        valid = len(part)
        part = np.pad(part, ((0, CHUNK_TICKS - valid), (0, 0)))[None]
        logits = model(
            part.astype(np.float32, copy=False),
            level_condition,
        ).reshape(-1)[:valid]
        probability = 1.0 / (1.0 + np.exp(-np.clip(logits, -80, 80)))
        chunks.append(probability >= args.threshold)
    onset_ticks = np.flatnonzero(np.concatenate(chunks)).tolist()

    args.output.mkdir(parents=True, exist_ok=True)
    title = args.title or args.audio.stem
    maidata = render_maidata(
        onset_ticks,
        bpm,
        offset,
        title,
        args.level,
        len(features),
    )
    (args.output / "maidata.txt").write_text(maidata, encoding="utf-8")
    report = {
        "audio": str(args.audio),
        "bpm": bpm,
        "offset_seconds": offset,
        "level": args.level,
        "ticks": len(features),
        "predicted_onsets": len(onset_ticks),
        "device": "numpy-cpu",
        "decoder": "joint",
        "threshold": args.threshold,
    }
    (args.output / "generation.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)
    print(f"Wrote {args.output / 'maidata.txt'}", flush=True)


if __name__ == "__main__":
    main()
