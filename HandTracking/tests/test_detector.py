import numpy as np
import pytest

from conftest import fake_result, make_hand
from hand_detector import (
    LEFT,
    RIGHT,
    Detection,
    HandDetector,
    HandTracker,
    clamp01,
    normalise_label,
    parse_result,
    resolve_labels,
)


# ---------------------------------------------------------- coordinate handling
def test_parse_result_without_mirror_keeps_coordinates():
    lm = make_hand(center=(0.3, 0.5))
    dets = parse_result(fake_result([("Right", 0.9, lm)]), mirror=False, swap_handedness=False)
    assert len(dets) == 1
    assert np.allclose(dets[0].landmarks, lm)
    assert dets[0].label == RIGHT and dets[0].score == pytest.approx(0.9)


def test_parse_result_mirror_flips_x_only():
    lm = make_hand(center=(0.3, 0.5))
    d = parse_result(fake_result([("Left", 0.8, lm)]), mirror=True, swap_handedness=False)[0]
    assert np.allclose(d.landmarks[:, 0], 1.0 - lm[:, 0])
    assert np.allclose(d.landmarks[:, 1:], lm[:, 1:])
    assert d.label == LEFT  # mirroring the OUTPUT does not change identity


def test_swap_handedness():
    lm = make_hand()
    d = parse_result(fake_result([("Left", 0.8, lm)]), mirror=True, swap_handedness=True)[0]
    assert d.label == RIGHT
    assert normalise_label("right", False) == RIGHT
    assert normalise_label("Right", True) == LEFT
    assert normalise_label("", False) == LEFT


def test_parse_result_skips_bad_hands():
    good = make_hand()
    nan = good.copy()
    nan[3, 0] = np.nan
    short = good[:20]
    dets = parse_result(fake_result([("Left", 0.9, nan), ("Right", 0.9, short), ("Right", 0.9, good)]), False, False)
    assert len(dets) == 1


def test_parse_empty_result():
    from types import SimpleNamespace

    assert parse_result(SimpleNamespace(hand_landmarks=[], handedness=[]), True, False) == []
    assert parse_result(None, True, False) == []


def test_clamp01():
    lm = np.array([[-0.2, 1.3, -0.5]] * 21)
    c = clamp01(lm)
    assert c[0, 0] == 0.0 and c[0, 1] == 1.0 and c[0, 2] == -0.5


# ------------------------------------------------------------- label resolution
@pytest.mark.parametrize("mirror", [True, False])
def test_duplicate_labels_resolved_by_position(mirror):
    left_side = make_hand(center=(0.25, 0.6))
    right_side = make_hand(center=(0.75, 0.6))
    dets = [Detection(RIGHT, 0.9, left_side), Detection(RIGHT, 0.8, right_side)]
    out = resolve_labels(dets, mirror=mirror)
    by_label = {d.label: d for d in out}
    assert set(by_label) == {LEFT, RIGHT}
    screen_right = by_label[RIGHT].landmarks[0, 0] > by_label[LEFT].landmarks[0, 0]
    # mirrored selfie view: user's right hand is on the screen's right
    assert screen_right == mirror


def test_distinct_labels_untouched_and_extra_hands_trimmed():
    a, b, c = (make_hand(center=(x, 0.5)) for x in (0.2, 0.5, 0.8))
    out = resolve_labels([Detection(LEFT, 0.5, a), Detection(RIGHT, 0.9, b), Detection(LEFT, 0.7, c)], True)
    assert len(out) == 2
    assert {d.label for d in out} == {LEFT, RIGHT}


# ---------------------------------------------------------------- tracker state
def test_tracker_hold_then_drop(cfg):
    cfg.hand_hold_ms = 150
    tr = HandTracker(cfg)
    lm = make_hand()
    out = tr.update([Detection(LEFT, 0.9, lm)], now=1.0, aspect=16 / 9)
    assert [h.state for h in out] == ["tracked"]
    out = tr.update([], now=1.1, aspect=16 / 9)
    assert [h.state for h in out] == ["held"]
    out = tr.update([], now=1.2, aspect=16 / 9)
    assert out == []
    # recovery: a new detection starts a fresh, unsmoothed track
    lm2 = make_hand(center=(0.2, 0.3))
    out = tr.update([Detection(LEFT, 0.9, lm2)], now=1.3, aspect=16 / 9)
    assert out[0].state == "tracked" and np.allclose(out[0].landmarks, lm2)


def test_tracker_orders_left_then_right(cfg):
    tr = HandTracker(cfg)
    out = tr.update([Detection(RIGHT, 0.9, make_hand()), Detection(LEFT, 0.9, make_hand())], 0.0, 16 / 9)
    assert [h.label for h in out] == [LEFT, RIGHT]


def test_tracker_smoothing_reduces_jitter(cfg):
    rng = np.random.default_rng(1)
    tr = HandTracker(cfg)
    base = make_hand()
    raw_dev, smooth_dev = [], []
    for i in range(120):
        noisy = base + rng.normal(0, 0.004, base.shape)
        h = tr.update([Detection(LEFT, 0.9, noisy)], i / 30.0, 16 / 9)[0]
        if i > 30:
            raw_dev.append(np.abs(noisy - base).mean())
            smooth_dev.append(np.abs(h.landmarks - base).mean())
    assert np.mean(smooth_dev) < np.mean(raw_dev) * 0.7


def test_tracker_reports_pose(cfg):
    tr = HandTracker(cfg)
    out = tr.update([Detection(LEFT, 0.9, make_hand((False, True, True, False, False)))], 0.0, 16 / 9)
    assert out[0].pose == "peace"


# ------------------------------------------- detector with a mocked MediaPipe
class FakeLandmarker:
    def __init__(self, results=None, fail=False):
        self.results = results or []
        self.fail = fail
        self.timestamps = []
        self.closed = False

    def detect_for_video(self, image, ts_ms):
        self.timestamps.append(ts_ms)
        if self.fail:
            raise RuntimeError("boom")
        return self.results.pop(0) if self.results else fake_result([])

    def close(self):
        self.closed = True


def _detector(cfg, lm):
    det = HandDetector(cfg)
    det._landmarker = lm
    det.status = "ok"
    return det


def test_detector_end_to_end_with_mock(cfg):
    pytest.importorskip("mediapipe")
    hand = make_hand(center=(0.3, 0.5))
    fake = FakeLandmarker([fake_result([("Right", 0.95, hand)])])
    det = _detector(cfg, fake)
    frame = np.zeros((720, 1280, 3), np.uint8)
    out = det.process(frame, 10.0)
    assert len(out) == 1 and out[0].label == RIGHT
    assert np.allclose(out[0].landmarks[:, 0], 1.0 - hand[:, 0])  # mirrored by default


def test_detector_timestamps_strictly_increase(cfg):
    pytest.importorskip("mediapipe")
    fake = FakeLandmarker()
    det = _detector(cfg, fake)
    frame = np.zeros((480, 640, 3), np.uint8)
    for t in (5.0, 5.0, 4.9, 5.0004, 6.0):
        det.process(frame, t)
    ts = fake.timestamps
    assert all(b > a for a, b in zip(ts, ts[1:])), ts


def test_detector_recovers_after_repeated_errors(cfg, monkeypatch):
    pytest.importorskip("mediapipe")
    fake = FakeLandmarker(fail=True)
    det = _detector(cfg, fake)
    frame = np.zeros((480, 640, 3), np.uint8)
    for i in range(HandDetector.MAX_CONSECUTIVE_ERRORS):
        assert det.process(frame, float(i)) == []  # never raises
    assert fake.closed and det._landmarker is None and det.status == "restarting"

    reopened = FakeLandmarker()

    def fake_open():
        det._landmarker = reopened
        det.status = "ok"

    monkeypatch.setattr(det, "open", fake_open)
    det.process(frame, 100.0)
    assert det.status == "ok" and reopened.timestamps


def test_detector_model_failure_is_reported_not_raised(cfg, monkeypatch):
    det = HandDetector(cfg)

    def bad_open():
        raise RuntimeError("model missing")

    monkeypatch.setattr(det, "open", bad_open)
    assert det.process(np.zeros((480, 640, 3), np.uint8), 1.0) == []
    assert det.status == "error" and "model missing" in det.last_error


def test_downscale_respects_tracking_width(cfg):
    cfg.tracking_width = 320
    det = HandDetector(cfg)
    rgb = det._prepare(np.zeros((720, 1280, 3), np.uint8))
    assert rgb.shape == (180, 320, 3)
    small = det._prepare(np.zeros((120, 160, 3), np.uint8))
    assert small.shape == (120, 160, 3)  # never upscales
