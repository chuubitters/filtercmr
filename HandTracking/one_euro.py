"""Vectorised One Euro Filter (Casiez, Roussel & Vogel, CHI 2012).

Adaptive low-pass filter: heavy smoothing when a signal is slow (removes
jitter), light smoothing when it moves fast (removes lag). Operates on a
whole numpy array at once so one instance can smooth all 21x3 landmark
coordinates of a hand.
"""

from __future__ import annotations

import math
from typing import Optional

import numpy as np


def _alpha(cutoff: np.ndarray | float, dt: float) -> np.ndarray | float:
    tau = 1.0 / (2.0 * math.pi * cutoff)
    return 1.0 / (1.0 + tau / dt)


class OneEuroFilter:
    def __init__(self, min_cutoff: float = 1.0, beta: float = 0.0, d_cutoff: float = 1.0) -> None:
        if min_cutoff <= 0 or d_cutoff <= 0:
            raise ValueError("cutoff frequencies must be > 0")
        self.min_cutoff = float(min_cutoff)
        self.beta = float(beta)
        self.d_cutoff = float(d_cutoff)
        self._x: Optional[np.ndarray] = None
        self._dx: Optional[np.ndarray] = None
        self._t: Optional[float] = None

    def reset(self) -> None:
        self._x = None
        self._dx = None
        self._t = None

    @property
    def initialised(self) -> bool:
        return self._x is not None

    def __call__(self, x: np.ndarray, t: float) -> np.ndarray:
        """Filter sample ``x`` taken at time ``t`` (seconds, monotonic)."""
        x = np.asarray(x, dtype=np.float64)
        if self._x is None or self._t is None or self._x.shape != x.shape:
            self._x = x.copy()
            self._dx = np.zeros_like(x)
            self._t = t
            return self._x.copy()

        dt = t - self._t
        if dt <= 0.0:
            # Duplicate / out-of-order timestamp: keep the previous estimate.
            return self._x.copy()

        dx = (x - self._x) / dt
        a_d = _alpha(self.d_cutoff, dt)
        dx_hat = a_d * dx + (1.0 - a_d) * self._dx

        cutoff = self.min_cutoff + self.beta * np.abs(dx_hat)
        a = _alpha(cutoff, dt)
        x_hat = a * x + (1.0 - a) * self._x

        self._x, self._dx, self._t = x_hat, dx_hat, t
        return x_hat.copy()
