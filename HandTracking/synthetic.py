"""Synthetic hand landmarks (no camera needed).

Used by the test suite and by tools/fake_tracker.py. Produces 21 MediaPipe-style
landmarks in normalised image coordinates for a chosen finger configuration.
"""

import math

import numpy as np

ASPECT = 16 / 9


def make_hand(ext=(False, True, False, False, False), angle_deg=0.0, center=(0.5, 0.6), scale=0.12, aspect=ASPECT,
              thumb_bend=0.0, pinch=False):
    """Synthetic 21-landmark hand in normalised image coordinates.

    Built in an isotropic frame (unit = hand size, y down, fingers pointing
    up), rotated by ``angle_deg`` and mapped into [0,1] image space.
    ``thumb_bend`` (radians) bends an extended thumb at its IP joint, like real
    "L" shapes. ``pinch`` curls the index toward the thumb until the tips touch.
    """
    up = np.array([0.0, -1.0])
    down = -up
    p = np.zeros((21, 2))
    p[0] = (0, 0)
    # thumb
    p[1] = (-0.25, -0.2)
    p[2] = (-0.45, -0.4)
    if ext[0]:
        # Thumb spread sideways-up, as in an "L" / open hand.
        ip = np.array([-0.65, -0.45])
        d = np.array([-0.23, -0.07])
        c, s = math.cos(thumb_bend), math.sin(thumb_bend)
        p[3], p[4] = ip, ip + np.array([c * d[0] - s * d[1], s * d[0] + c * d[1]])
    else:
        p[3], p[4] = (-0.4, -0.6), (-0.1, -0.65)
    mcps = {5: (-0.3, -1.0), 9: (-0.1, -1.05), 13: (0.1, -1.0), 17: (0.3, -0.9)}
    for finger, (mcp_i, mcp) in enumerate(mcps.items(), start=1):
        mcp = np.array(mcp)
        p[mcp_i] = mcp
        if ext[finger]:
            pip = mcp + up * 0.4
            dip = pip + up * 0.25
            tip = dip + up * 0.2
        else:
            pip = mcp + up * 0.3
            dip = pip + down * 0.2
            tip = dip + down * 0.15
        p[mcp_i + 1], p[mcp_i + 2], p[mcp_i + 3] = pip, dip, tip

    if pinch:
        # Index curls down toward the thumb; thumb reaches up; tips touch.
        p[6], p[7], p[8] = (-0.33, -1.35), (-0.45, -1.33), (-0.52, -1.18)
        p[3], p[4] = (-0.6, -0.75), (-0.55, -1.12)

    a = math.radians(angle_deg)
    rot = np.array([[math.cos(a), -math.sin(a)], [math.sin(a), math.cos(a)]])
    p = p @ rot.T * scale
    out = np.zeros((21, 3))
    out[:, 0] = (center[0] * aspect + p[:, 0]) / aspect
    out[:, 1] = center[1] + p[:, 1]
    return out
