"""Listen for FILTER CMR tracker packets and print them.

Stand-in for the Unity receiver: use it to verify the UDP link before Unity
exists, or to debug what Unity should be receiving.

    python tools/udp_monitor.py               # summary line per packet (rate-limited)
    python tools/udp_monitor.py --raw         # dump raw JSON
    python tools/udp_monitor.py --port 5065
"""

from __future__ import annotations

import argparse
import json
import socket
import sys
import time


def summarise(msg: dict) -> str:
    hands = msg.get("hands", [])
    parts = []
    for h in hands:
        lm = h.get("lm", [])
        tip = (lm[8 * 3], lm[8 * 3 + 1]) if len(lm) >= 63 else (float("nan"),) * 2
        parts.append(
            f"{h.get('id','?')[0]}:{h.get('pose','?'):<5} {h.get('state','?')[0]} "
            f"idx=({tip[0]:.3f},{tip[1]:.3f}) ext={''.join(map(str, h.get('ext', [])))}"
        )
    cam, trk = msg.get("cam", {}), msg.get("trk", {})
    return (
        f"#{msg.get('seq'):>7} {msg.get('type','?'):<9} cam={cam.get('status')} "
        f"{cam.get('w')}x{cam.get('h')} trk={trk.get('status')} {trk.get('fps', 0):5.1f}fps "
        f"hands={len(hands)} " + " | ".join(parts)
    )


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=5065)
    ap.add_argument("--raw", action="store_true", help="print raw JSON")
    ap.add_argument("--hz", type=float, default=4.0, help="max summary lines per second (0 = all)")
    ap.add_argument("--count", type=int, default=0, help="exit after N packets (0 = forever)")
    ap.add_argument("--timeout", type=float, default=0.0, help="exit with code 2 if nothing arrives for N s")
    args = ap.parse_args(argv)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((args.host, args.port))
    sock.settimeout(0.5)
    print(f"listening on udp://{args.host}:{args.port}  (Ctrl+C to stop)", flush=True)

    received = lost = bad = 0
    last_seq = None
    last_print = 0.0
    last_rx = time.monotonic()
    t0 = time.monotonic()
    try:
        while True:
            try:
                data, _ = sock.recvfrom(65535)
            except socket.timeout:
                if args.timeout and time.monotonic() - last_rx > args.timeout:
                    print(f"no packets for {args.timeout}s", file=sys.stderr)
                    return 2
                continue
            last_rx = time.monotonic()
            try:
                msg = json.loads(data.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError):
                bad += 1
                continue
            received += 1
            seq = msg.get("seq")
            if isinstance(seq, int) and last_seq is not None and seq > last_seq + 1:
                lost += seq - last_seq - 1
            if isinstance(seq, int):
                last_seq = seq
            now = time.monotonic()
            if args.raw:
                print(data.decode("utf-8"), flush=True)
            elif args.hz <= 0 or now - last_print >= 1.0 / args.hz or msg.get("type") == "bye":
                last_print = now
                print(summarise(msg), flush=True)
            if args.count and received >= args.count:
                break
    except KeyboardInterrupt:
        pass
    finally:
        dt = max(time.monotonic() - t0, 1e-6)
        print(f"\nreceived={received} ({received/dt:.1f}/s) lost={lost} malformed={bad}", file=sys.stderr)
        sock.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
