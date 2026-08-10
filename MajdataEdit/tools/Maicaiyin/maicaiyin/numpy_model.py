from __future__ import annotations

from pathlib import Path

import numpy as np


class JointPlacementModel:
    """Small NumPy evaluator for the trained joint-placement PyTorch network."""

    def __init__(self, path: str | Path) -> None:
        self.weights = np.load(path, allow_pickle=False)

    def __call__(self, features: np.ndarray, difficulty: np.ndarray) -> np.ndarray:
        weights = self.weights
        tick_hidden = _conv1x1(
            features.transpose(0, 2, 1),
            weights["input_projection.weight"],
            weights["input_projection.bias"],
        )
        for index, dilation in enumerate((1, 3, 9, 27)):
            tick_hidden = self._residual_block(
                tick_hidden, f"tick_blocks.{index}", dilation
            )

        batch_size, hidden_size, tick_count = tick_hidden.shape
        window_count = tick_count // 48
        grouped = tick_hidden.reshape(
            batch_size, hidden_size, window_count, 48
        )
        phase_ordered = grouped.transpose(0, 2, 1, 3).reshape(
            batch_size, window_count, hidden_size * 48
        )
        context = _linear(
            phase_ordered,
            weights["window_projection.0.weight"],
            weights["window_projection.0.bias"],
        )
        context = _silu(context)
        context = _linear(
            context,
            weights["window_projection.3.weight"],
            weights["window_projection.3.bias"],
        ).transpose(0, 2, 1)
        for index, dilation in enumerate((1, 2, 4)):
            context = self._residual_block(
                context, f"pattern_blocks.{index}", dilation
            )
        audio_context = context.transpose(0, 2, 1)

        condition = _linear(
            difficulty[:, None],
            weights["difficulty_embedding.0.weight"],
            weights["difficulty_embedding.0.bias"],
        )
        condition = _silu(condition)
        condition = _linear(
            condition,
            weights["difficulty_embedding.2.weight"],
            weights["difficulty_embedding.2.bias"],
        )
        prior = _linear(
            _silu(
                _linear(
                    condition,
                    weights["prior_head.0.weight"],
                    weights["prior_head.0.bias"],
                )
            ),
            weights["prior_head.2.weight"],
            weights["prior_head.2.bias"],
        )[:, None, :]

        audio_embedding = _normalize(
            _linear(
                audio_context,
                weights["audio_projection.weight"],
                weights["audio_projection.bias"],
            )
        )
        pattern_embedding = _normalize(weights["pattern_embeddings"])
        compatibility = 10.0 * np.einsum(
            "bwh,vh->bwv", audio_embedding, pattern_embedding
        )
        density = _log_softmax(
            _linear(
                _silu(
                    _linear(
                        audio_context,
                        weights["density_head.0.weight"],
                        weights["density_head.0.bias"],
                    )
                ),
                weights["density_head.2.weight"],
                weights["density_head.2.bias"],
            ),
            axis=-1,
        )
        density_boost = density[..., weights["pattern_density_buckets"]]
        pattern_logits = prior + compatibility + np.float32(0.85) * density_boost

        tick_by_window = tick_hidden.reshape(
            batch_size, hidden_size, window_count, 48
        ).transpose(0, 2, 3, 1)
        context_by_tick = np.broadcast_to(
            (audio_context + condition[:, None, :])[:, :, None, :],
            tick_by_window.shape,
        )
        placement_hidden = np.concatenate(
            (tick_by_window, context_by_tick), axis=-1
        )
        placement_logits = _linear(
            _silu(
                _linear(
                    placement_hidden,
                    weights["placement_head.0.weight"],
                    weights["placement_head.0.bias"],
                )
            ),
            weights["placement_head.2.weight"],
            weights["placement_head.2.bias"],
        ).squeeze(-1)

        expected_pattern = _softmax(pattern_logits, axis=-1) @ weights[
            "pattern_masks"
        ]
        expected_pattern = np.clip(expected_pattern, 1e-4, 1 - 1e-4)
        template_logits = np.log(expected_pattern / (1 - expected_pattern))
        output = placement_logits + weights["placement_template_scale"] * template_logits
        return output.reshape(batch_size, -1).astype(np.float32, copy=False)

    def _residual_block(
        self, inputs: np.ndarray, prefix: str, dilation: int
    ) -> np.ndarray:
        weights = self.weights
        hidden = _depthwise_conv(
            inputs,
            weights[f"{prefix}.depthwise.weight"],
            weights[f"{prefix}.depthwise.bias"],
            dilation,
        )
        hidden = _conv1x1(
            hidden,
            weights[f"{prefix}.pointwise.weight"],
            weights[f"{prefix}.pointwise.bias"],
        )
        value, gate = np.split(hidden, 2, axis=1)
        hidden = value * _sigmoid(gate)
        hidden = _conv1x1(
            hidden,
            weights[f"{prefix}.output.weight"],
            weights[f"{prefix}.output.bias"],
        )
        return _group_norm(
            inputs + hidden,
            weights[f"{prefix}.norm.weight"],
            weights[f"{prefix}.norm.bias"],
        )


def _linear(inputs: np.ndarray, weight: np.ndarray, bias: np.ndarray) -> np.ndarray:
    return inputs @ weight.T + bias


def _conv1x1(
    inputs: np.ndarray, weight: np.ndarray, bias: np.ndarray
) -> np.ndarray:
    return np.einsum("oi,bil->bol", weight[:, :, 0], inputs) + bias[None, :, None]


def _depthwise_conv(
    inputs: np.ndarray,
    weight: np.ndarray,
    bias: np.ndarray,
    dilation: int,
) -> np.ndarray:
    length = inputs.shape[-1]
    padded = np.pad(inputs, ((0, 0), (0, 0), (dilation, dilation)))
    output = np.zeros_like(inputs)
    for kernel_index in range(3):
        start = kernel_index * dilation
        output += (
            padded[:, :, start : start + length]
            * weight[:, 0, kernel_index][None, :, None]
        )
    return output + bias[None, :, None]


def _group_norm(
    inputs: np.ndarray, weight: np.ndarray, bias: np.ndarray
) -> np.ndarray:
    mean = inputs.mean(axis=(1, 2), keepdims=True)
    variance = inputs.var(axis=(1, 2), keepdims=True)
    normalized = (inputs - mean) / np.sqrt(variance + np.float32(1e-5))
    return normalized * weight[None, :, None] + bias[None, :, None]


def _sigmoid(inputs: np.ndarray) -> np.ndarray:
    return 1.0 / (1.0 + np.exp(-np.clip(inputs, -80, 80)))


def _silu(inputs: np.ndarray) -> np.ndarray:
    return inputs * _sigmoid(inputs)


def _normalize(inputs: np.ndarray) -> np.ndarray:
    denominator = np.linalg.norm(inputs, axis=-1, keepdims=True)
    return inputs / np.maximum(denominator, np.float32(1e-12))


def _softmax(inputs: np.ndarray, axis: int) -> np.ndarray:
    shifted = inputs - inputs.max(axis=axis, keepdims=True)
    exponent = np.exp(shifted)
    return exponent / exponent.sum(axis=axis, keepdims=True)


def _log_softmax(inputs: np.ndarray, axis: int) -> np.ndarray:
    shifted = inputs - inputs.max(axis=axis, keepdims=True)
    return shifted - np.log(np.exp(shifted).sum(axis=axis, keepdims=True))
