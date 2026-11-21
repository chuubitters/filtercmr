#!/usr/bin/env bash
# Start the FILTER CMR tracker. Extra arguments are passed to main.py,
# e.g.  ./run.sh --no-preview --camera-index 1
set -euo pipefail
cd "$(dirname "$0")"
if [ ! -d .venv ]; then
  echo "Run ./setup.sh first." >&2
  exit 1
fi
# shellcheck disable=SC1091
source .venv/bin/activate
exec python main.py "$@"
