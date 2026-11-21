import pytest

from conftest import make_hand
from finger_state import classify_pose, finger_extended

POSES = {
    "point": (False, True, False, False, False),
    "peace": (False, True, True, False, False),
    "frame": (True, True, False, False, False),
    "open": (True, True, True, True, True),
    "fist": (False, False, False, False, False),
}


@pytest.mark.parametrize("angle", [-75, -40, 0, 25, 60, 90, 180])
@pytest.mark.parametrize("pose", list(POSES))
def test_pose_detection_is_rotation_invariant(pose, angle):
    lm = make_hand(POSES[pose], angle_deg=angle)
    ext = finger_extended(lm)
    assert ext[1:] == list(POSES[pose][1:]), (pose, angle, ext)
    assert classify_pose(ext) == pose


@pytest.mark.parametrize("scale", [0.05, 0.12, 0.3])
def test_scale_invariant(scale):
    assert classify_pose(finger_extended(make_hand(POSES["peace"], scale=scale))) == "peace"


def test_thumb_flag():
    assert finger_extended(make_hand((True, True, True, True, True)))[0] is True
    assert finger_extended(make_hand((False, False, False, False, False)))[0] is False


def test_thumb_separates_point_from_frame():
    assert classify_pose([False, True, False, False, False]) == "point"
    assert classify_pose([True, True, False, False, False]) == "frame"   # L shape: thumb + index


def test_thumb_ignored_for_peace():
    assert classify_pose([True, True, True, False, False]) == "peace"
    assert classify_pose([False, True, True, False, False]) == "peace"
    assert classify_pose([False, True, False, True, False]) == "other"
