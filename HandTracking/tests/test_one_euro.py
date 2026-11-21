import numpy as np
import pytest

from one_euro import OneEuroFilter


def test_first_sample_passes_through():
    f = OneEuroFilter(1.0, 0.0)
    x = np.array([0.2, 0.4, 0.6])
    assert np.allclose(f(x, 0.0), x)
    assert f.initialised


def test_reduces_jitter_on_static_signal():
    rng = np.random.default_rng(0)
    f = OneEuroFilter(min_cutoff=1.0, beta=0.0)
    truth = np.full(63, 0.5)
    raw, out = [], []
    for i in range(300):
        noisy = truth + rng.normal(0, 0.005, size=63)
        raw.append(noisy)
        out.append(f(noisy, i / 30.0))
    raw, out = np.array(raw[50:]), np.array(out[50:])
    assert out.std(axis=0).mean() < raw.std(axis=0).mean() * 0.5


def test_beta_reduces_lag_on_fast_motion():
    def lag(beta):
        f = OneEuroFilter(min_cutoff=1.0, beta=beta)
        err = []
        for i in range(60):
            t = i / 30.0
            x = np.array([min(1.0, t * 2.0)])  # fast ramp (2 units/s)
            err.append(abs(f(x, t)[0] - x[0]))
        return float(np.mean(err[5:30]))

    assert lag(6.0) < lag(0.0) * 0.6


def test_non_increasing_timestamp_is_ignored():
    f = OneEuroFilter(1.0, 0.0)
    f(np.array([0.0]), 1.0)
    a = f(np.array([1.0]), 1.0)  # same timestamp
    b = f(np.array([1.0]), 0.5)  # going backwards
    assert a[0] == 0.0 and b[0] == 0.0


def test_reset_and_shape_change():
    f = OneEuroFilter(1.0, 0.0)
    f(np.zeros(3), 0.0)
    f.reset()
    assert not f.initialised
    out = f(np.ones(5), 1.0)  # new shape re-initialises
    assert out.shape == (5,) and np.allclose(out, 1.0)


def test_invalid_cutoff():
    with pytest.raises(ValueError):
        OneEuroFilter(min_cutoff=0)
