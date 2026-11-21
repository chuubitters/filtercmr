import sys
from pathlib import Path
from types import SimpleNamespace

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from synthetic import ASPECT, make_hand  # noqa: E402  (re-exported for tests)

__all__ = ["ASPECT", "make_hand", "fake_result"]


def fake_result(hands):
    """Duck-typed HandLandmarkerResult. ``hands`` = [(label, score, (21,3) array)]."""
    hand_landmarks, handedness = [], []
    for label, score, lm in hands:
        hand_landmarks.append([SimpleNamespace(x=float(x), y=float(y), z=float(z)) for x, y, z in lm])
        handedness.append([SimpleNamespace(category_name=label, display_name=label, score=score, index=0)])
    return SimpleNamespace(hand_landmarks=hand_landmarks, handedness=handedness, hand_world_landmarks=[])


@pytest.fixture
def cfg():
    from config import TrackerConfig

    c = TrackerConfig()
    c.preview = False
    return c
