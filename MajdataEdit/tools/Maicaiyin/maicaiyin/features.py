from __future__ import annotations

import librosa
import numpy as np
from scipy.ndimage import median_filter

FEATURE_SIZE = 397


def _interpolate(frames, frame_times, target_times):
    output = np.empty((len(target_times), frames.shape[0]), dtype=np.float32)
    for index, values in enumerate(frames):
        output[:, index] = np.interp(
            target_times,
            frame_times,
            values,
            left=float(values[0]),
            right=float(values[-1]),
        )
    return output


def _rhythm_features(audio, times, sample_rate):
    hop = max(1, round(sample_rate / 100))
    mel = librosa.feature.melspectrogram(
        y=audio,
        sr=sample_rate,
        n_fft=1024,
        hop_length=hop,
        n_mels=96,
        fmin=30,
        fmax=sample_rate / 2,
        power=2.0,
    )
    log_mel = librosa.power_to_db(mel, ref=np.max)
    frame_times = librosa.frames_to_time(
        np.arange(log_mel.shape[1]), sr=sample_rate, hop_length=hop
    )
    aligned = _interpolate(log_mel, frame_times, times)
    delta = _interpolate(
        librosa.feature.delta(log_mel, width=9, mode="interp"),
        frame_times,
        times,
    )
    positive_flux = np.maximum(
        np.diff(log_mel, axis=1, prepend=log_mel[:, :1]), 0
    )
    band_flux = np.stack(
        [band.mean(axis=0) for band in np.array_split(positive_flux, 8)],
        axis=0,
    )
    duration = len(audio) / sample_rate
    contexts = [
        _interpolate(
            band_flux,
            frame_times,
            np.clip(times + offset, 0, duration),
        )
        for offset in (-0.04, -0.02, 0.0, 0.02, 0.04)
    ]
    return np.concatenate(
        (
            np.clip((aligned + 80) / 80, 0, 1),
            np.clip(delta / 10, -1, 1),
            *[np.clip(context / 20, 0, 1) for context in contexts],
        ),
        axis=1,
    ).astype(np.float32, copy=False)


def extract_features(audio, times, sample_rate):
    """Reproduce the 397-dimensional stem-rhythm-v2 training features."""
    waveform = np.asarray(audio, dtype=np.float32)
    base = _rhythm_features(waveform, times, sample_rate)
    mix_db = base[:, :96] * 80 - 80
    mix_power = np.power(10.0, mix_db / 10.0)
    harmonic_evidence = median_filter(mix_power, size=(17, 1), mode="nearest")
    percussive_evidence = median_filter(mix_power, size=(1, 9), mode="nearest")
    denominator = (
        np.square(harmonic_evidence) + np.square(percussive_evidence) + 1e-10
    )
    harmonic = mix_power * np.square(harmonic_evidence) / denominator
    percussive = mix_power * np.square(percussive_evidence) / denominator
    harmonic_db = 10 * np.log10(np.maximum(harmonic, 1e-8))
    percussive_db = 10 * np.log10(np.maximum(percussive, 1e-8))
    harmonic_48 = harmonic_db.reshape(len(times), 48, 2).mean(axis=2)
    percussive_48 = percussive_db.reshape(len(times), 48, 2).mean(axis=2)
    flux = np.maximum(
        np.diff(percussive_db, axis=0, prepend=percussive_db[:1]), 0
    )
    band_flux = np.stack(
        [band.mean(axis=1) for band in np.split(flux, 8, axis=1)], axis=1
    )
    duration = len(waveform) / sample_rate
    contexts = [
        _interpolate(
            band_flux.T,
            times,
            np.clip(times + offset, 0, duration),
        )
        for offset in (-0.04, -0.02, 0.0, 0.02, 0.04)
    ]
    stem = np.concatenate(
        (
            base,
            np.clip((percussive_48 + 80) / 80, 0, 1),
            *[np.clip(context / 20, 0, 1) for context in contexts],
            np.clip((harmonic_48 + 80) / 80, 0, 1),
        ),
        axis=1,
    )
    center = stem[:, 280:320].reshape(len(times), 5, 8)[:, 2, :]
    scale_blocks = []
    activity = []
    for radius in (6, 12, 24):
        kernel = np.ones(2 * radius + 1, dtype=np.float32)
        kernel /= kernel.sum()
        smoothed = np.empty_like(center)
        for band in range(8):
            values = np.convolve(center[:, band], kernel, mode="same")
            smoothed[:, band] = values[: len(center)]
        smoothed = np.clip(smoothed, 0, 1)
        scale_blocks.append(smoothed)
        activity.append(smoothed.mean(axis=1, keepdims=True))
    audio_features = np.concatenate((stem, *scale_blocks, *activity), axis=1)
    phase = np.arange(len(times), dtype=np.float32) % 24
    angle = 2 * np.pi * phase / 24
    output = np.concatenate(
        (audio_features, np.sin(angle)[:, None], np.cos(angle)[:, None]),
        axis=1,
    )
    if output.shape[1] != FEATURE_SIZE:
        raise RuntimeError(f"unexpected feature size: {output.shape[1]}")
    return output.astype(np.float32, copy=False)
