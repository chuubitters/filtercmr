# UDP Protocol — Python tracker → Unity

**Version:** 1  ·  **Transport:** UDP, one JSON object per datagram  ·  **Default target:** `127.0.0.1:5065`

Reference implementations: sender `HandTracking/udp_sender.py`, receiver `UnityProject/Assets/FILTERCMR/Scripts/Networking/HandDataParser.cs` (tested against packets produced by the sender).

The tracker is the only sender; Unity only listens. UDP is fire-and-forget: packets may be dropped or (off-loopback) reordered. Receivers must use `seq` to ignore stale packets and must never block waiting for one.

## Message types

| `type`      | When                                                                 |
|-------------|----------------------------------------------------------------------|
| `frame`     | After every processed camera frame (≈ camera FPS, capped by `--max-tracking-fps`). |
| `heartbeat` | When no new frame is available, at `--heartbeat-hz` (default 5 Hz). Lets Unity distinguish "tracker alive, camera down" from "tracker not running". |
| `bye`       | Once, on clean shutdown. Unity can switch to "tracker offline" immediately instead of waiting for a timeout. |

## Schema

```jsonc
{
  "type": "frame",            // "frame" | "heartbeat" | "bye"
  "v": 1,                     // protocol version
  "seq": 1234,                // increases by 1 per packet (wraps at 2^31)
  "t": 81234567.8,            // sender monotonic clock, ms (only meaningful as a difference)
  "cam": {
    "status": "ok",           // "starting" | "ok" | "reconnecting" | "error" | "stopped"
    "w": 1280, "h": 720,      // actual capture size (0 until the first frame)
    "mirrored": true          // x has been mirrored (selfie view)
  },
  "trk": {
    "status": "ok",           // "starting" | "ok" | "error" | "restarting" | "stopped"
    "fps": 29.9,              // detections per second (EMA)
    "latency": 14.2           // ms from frame grab to packet build
  },
  "hands": [                  // 0–2 entries, always Left before Right
    {
      "id": "Left",           // "Left" | "Right" — the USER's hand, unique per packet
      "score": 0.97,          // handedness confidence
      "state": "tracked",     // "tracked" (seen this frame) | "held" (briefly lost, last pose)
      "age": 0.0,             // ms since this hand was last actually detected
      "ext": [0,1,0,0,0],     // advisory finger extension: thumb,index,middle,ring,pinky
      "pose": "point",        // "point" (index only) | "frame" (thumb + index L) | "pinch" | "peace" | "open" | "fist" | "other"
      "pinch": 1.15,          // thumb-tip <-> index-tip distance / palm size (small = touching)
      "lm": [x0,y0,z0, x1,y1,z1, … x20,y20,z20]   // 63 floats, 21 landmarks
    }
  ]
}
```

## Coordinates

* `x`, `y` are normalised to **0.0–1.0** of the camera image and clamped to that range. Origin is **top-left**, **y points down** (image convention).
* If `cam.mirrored` is `true`, `x` is already mirrored (`x = 1 - x_raw`). Unity must display its webcam texture mirrored the same way, so a landmark at `x` lines up with the image at `x`.
* Unity UV conversion: `u = x`, `v = 1 - y`.
* `z` is MediaPipe's relative depth (wrist ≈ 0, smaller = closer to the camera, roughly the same scale as `x`). It is not clamped and is not a metric distance.
* Aspect ratio: normalised coordinates are relative to the full image. If Unity's webcam texture has a different aspect ratio from `cam.w / cam.h`, the overlays will be misaligned. Unity should compare the two and warn.

## Landmark indices (MediaPipe)

```
0 wrist
1 thumb_cmc   2 thumb_mcp   3 thumb_ip    4 thumb_tip
5 index_mcp   6 index_pip   7 index_dip   8 index_tip
9 middle_mcp 10 middle_pip 11 middle_dip 12 middle_tip
13 ring_mcp  14 ring_pip   15 ring_dip   16 ring_tip
17 pinky_mcp 18 pinky_pip  19 pinky_dip  20 pinky_tip
```

Index of landmark *i*, axis *a* (0=x, 1=y, 2=z) in `lm`: `i * 3 + a`.

## Receiver rules (Unity)

1. Receive on a background thread; hand the newest packet to the main thread (latest-wins, no queue).
2. Drop packets whose `seq` is not newer than the last accepted one, unless the jump backwards is large (> 1000), which means the tracker restarted.
3. Ignore packets with an unknown `v` or malformed JSON; count them for the debug overlay.
4. **Tracker timeout:** no packet for 1 s → "tracker offline". Any packet → back online.
5. **Hand timeout:** a hand is present only while it appears in the latest packet. `held` hands may be drawn but should not start a new gesture.
6. `pose` is the authoritative per-hand classification (made by Python). Unity combines the two hands' poses (point+point, frame+frame) into the two-hand gestures and debounces them over time; it does not re-classify finger geometry. `ext` is informational.

## Size

A two-hand `frame` is roughly 1–1.5 kB, under a single Ethernet MTU, so the protocol also works across a LAN. The sender refuses anything over 8 kB.

## Video forwarding (port 5066)

This is optional and off by default. It's enabled with `./run.sh --stream-video`. It is a fallback for Macs where Unity can't open the webcam while the tracker is using it. It is a **separate** stream; the JSON messages above are unchanged.

| | |
|---|---|
| Port | `--video-port`, default **5066** (must differ from the tracking port) |
| Content | Raw camera frame (**not** mirrored), JPEG, `--video-width` (640) px wide, `--video-quality` (70) |
| Rate | At most the capture FPS, sent right after each tracking packet |
| Datagram | 20-byte big-endian header + ≤ 8000 bytes of JPEG (fits macOS's default 9216-byte UDP limit) |

```
offset size  field
0      4     magic "FCV1"
4      4     frame_id   uint32, +1 per frame (wraps)
8      2     index      uint16, chunk index 0..count-1
10     2     count      uint16, chunks in this frame
12     4     total_len  uint32, JPEG size in bytes
16     2     width      uint16, frame width
18     2     height     uint16, frame height
20     …     payload    bytes [index·8000, index·8000 + len) of the JPEG
```

Receiver rules: chunks may arrive in any order, and duplicates are ignored. A newer `frame_id` abandons an incomplete older frame, which is counted as dropped. A frame is only delivered when all `count` chunks have arrived. The reference implementations are `HandTracking/video_stream.py` and `Networking/VideoStreamCore.cs`.

## Changelog

* **Pose `frame` added** (still protocol v1): thumb + index extended, other fingers folded. Index-only with the thumb out is now `frame` rather than `point`. Receivers that don't know `frame` treat it as an unknown pose. The Unity parser in this repo supports it.
* **Pose `pinch` and field `pinch` added** (still protocol v1). `pinch` = thumb tip touching index tip, with the index raised so a fist doesn't count. Thresholds: `--pinch-ratio` (0.25) and `--thumb-out-ratio` (0.5). Unity toggles halftone on a pinch.
