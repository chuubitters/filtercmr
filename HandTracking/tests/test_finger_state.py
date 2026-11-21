import pytest

from conftest import make_hand
from finger_state import classify_hand, classify_pose, finger_extended

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


L_SHAPE = (True, True, False, False, False)


@pytest.mark.parametrize("bend", [-0.9, -0.5, 0.0, 0.5, 0.9])
@pytest.mark.parametrize("angle", [-60, 0, 45, 90, 180])
def test_real_l_shapes_with_bent_thumb_are_frame(bend, angle):
    # Regression: real thumbs bend at the IP joint; the old straightness test rejected them.
    _, pose, _ = classify_hand(make_hand(L_SHAPE, angle_deg=angle, thumb_bend=bend))
    assert pose == "frame"


@pytest.mark.parametrize("angle", [-60, 0, 45, 90, 180])
@pytest.mark.parametrize("scale", [0.06, 0.12, 0.25])
def test_pinch_detected_at_any_rotation_and_distance(angle, scale):
    _, pose, ratio = classify_hand(make_hand(POSES["point"], angle_deg=angle, scale=scale, pinch=True))
    assert pose == "pinch" and ratio < 0.25


@pytest.mark.parametrize("pose_name", ["point", "frame", "peace", "open", "fist"])
def test_other_poses_are_not_pinch(pose_name):
    _, pose, ratio = classify_hand(make_hand(POSES[pose_name]))
    assert pose == pose_name and ratio > 0.25


def test_thresholds_are_tunable():
    lm = make_hand(L_SHAPE)
    assert classify_hand(lm, thumb_out=5.0)[1] == "point"      # impossible threshold -> thumb not out
    assert classify_hand(lm, pinch_threshold=2.0)[1] == "pinch"  # huge threshold -> counts as pinch
