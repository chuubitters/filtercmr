# Testing

Status key: ✅ automated · 🖐 manual (needs a webcam and hands) · ⏳ arrives in a later milestone

## Automated

```bash
cd HandTracking && source .venv/bin/activate
python -m pytest -q tests          # 74 tests, < 2 s, no camera needed
python tools/check_setup.py        # environment: packages, model, MediaPipe, webcam, UDP
```

| Area | Test file | What is covered |
|---|---|---|
| One Euro Filter | `test_one_euro.py` | jitter reduction, lag reduction with β, bad timestamps, reset |
| Coordinate normalisation | `test_detector.py` | mirroring flips x only, clamping to 0–1, NaN/short hands rejected |
| Left/Right identity | `test_detector.py` | label swap, duplicate labels resolved by screen side (mirrored and not) |
| Tracking loss & recovery | `test_detector.py` | held → dropped after timeout, clean restart, MediaPipe auto-restart after 5 errors, model failure reported not raised |
| Tracking resolution | `test_detector.py` | downscale to `tracking_width`, never upscale |
| Finger states | `test_finger_state.py` | point / peace / open / fist at 7 rotations and 3 scales |
| UDP packets | `test_udp_and_config.py` | schema, size < 1500 B, clamping, NaN rejected, loopback round-trip, no listener = no crash |
| Main loop | `test_main_loop.py` | fake camera + mocked MediaPipe → real UDP: frame packets, ordering, mirroring, `bye` |
| CLI config | `test_udp_and_config.py` | flags and validation |
| Video forwarding | `test_video_stream.py` | chunking within macOS's 9216-byte datagram limit, reassembly, raw (un-mirrored) frames, rate limit, `--stream-video` flags |
| Main loop + video | `test_main_loop.py` | `--stream-video` sends chunks while tracking packets keep the exact v1 fields |
| Fake tracker | `test_main_loop.py` | `tools/fake_tracker.py` sends valid packets and `bye` |

The automated tests do **not** run the real MediaPipe neural network, because they can't contain real hand images. Real inference is covered by `check_setup.py` (blank frame) and by the manual tests below.

## Manual — Milestone 1

Run the tracker with the preview: `./run.sh`. In a second terminal run `python tools/udp_monitor.py`.

| # | Test | Steps | Expected |
|---|---|---|---|
| M1-1 🖐 | Webcam connection | `python tools/check_setup.py` | `[ OK ] webcam camera 0: 1280x720` (or your camera's size) |
| M1-2 🖐 | MediaPipe initialisation | same | `[ OK ] mediapipe init + inference … 0 hands` |
| M1-3 🖐 | One hand | Raise your **right** hand only | Preview shows a skeleton labelled `RIGHT`; monitor shows `hands=1 R:…` |
| M1-4 🖐 | Handedness | Raise your **left** hand only | Label `LEFT`. If labels are inverted, press **H** (or run with `--swap-handedness`) and note it in an issue |
| M1-5 🖐 | Mirroring | Move your right hand to your right | The skeleton moves right on screen; index-tip x in the monitor increases |
| M1-6 🖐 | Two hands | Raise both hands | `hands=2`, one `L`, one `R`, never two of the same |
| M1-7 🖐 | Index-only pose | Both hands: index up, others folded | Both labels show `[.I...] point` |
| M1-8 🖐 | Two-finger pose | Both hands: index + middle up (peace sign) | `[.IM..] peace` on both |
| M1-9 🖐 | Normalisation | Put your index tip in each screen corner | Coordinates read about `0.0,0.0` top-left and `1.0,1.0` bottom-right, never outside 0–1 |
| M1-10 🖐 | Smoothing | Hold still, press **S** to toggle | With smoothing on, points jitter visibly less; fast moves still follow closely |
| M1-11 🖐 | Hand loss | Drop one hand quickly out of view | Briefly `HELD` (amber), then it disappears; no crash |
| M1-12 🖐 | Recovery | Bring the hand back | Tracked again within a few frames |
| M1-13 🖐 | Camera unplug (USB cam) | Unplug while running, wait, re-plug | Log shows reopen attempts; monitor shows `cam=reconnecting` heartbeats; tracking resumes after re-plug |
| M1-14 🖐 | Reset | Press **R** | Tracks cleared; re-detected on the next frame |
| M1-15 ✅/🖐 | UDP | Monitor running | ~30 packets/s with hands, `lost=0` on exit; stop the tracker → monitor prints a `bye` line |
| M1-16 🖐 | FPS | Watch the preview header | Record the FPS and latency for your Mac in the issue tracker (no number is promised in advance) |

## Later milestones ⏳

**Milestone 2 (Unity integration):** see [UNITY_TESTING.md](UNITY_TESTING.md).

**Milestones 3 + 4 (masks, halftone shader):** see [MILESTONE_3_4_TESTING.md](MILESTONE_3_4_TESTING.md).
