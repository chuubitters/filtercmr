#!/usr/bin/env bash
# One-time setup for the FILTER CMR tracker (macOS / Linux).
# Creates HandTracking/.venv, installs dependencies, downloads the model,
# and runs the non-camera checks.
set -euo pipefail
cd "$(dirname "$0")"

PY="${PYTHON:-python3}"
if ! command -v "$PY" >/dev/null 2>&1; then
  echo "python3 not found. Install it (e.g. 'brew install python@3.12') and re-run." >&2
  exit 1
fi

if [ ! -d .venv ]; then
  echo "==> creating virtual environment (.venv) with $($PY --version)"
  "$PY" -m venv .venv
fi
# shellcheck disable=SC1091
source .venv/bin/activate
python -m pip install --upgrade pip >/dev/null
echo "==> installing dependencies"
python -m pip install -r requirements-dev.txt

echo "==> downloading MediaPipe hand model (if missing)"
python -c "from config import DEFAULT_MODEL_PATH; from hand_detector import ensure_model; print(ensure_model(DEFAULT_MODEL_PATH))"

echo "==> running unit tests"
python -m pytest -q tests

echo "==> environment check (camera skipped; run 'tools/check_setup.py' without flags to test it)"
python tools/check_setup.py --skip-camera

echo
echo "Setup complete. Start the tracker with:  ./run.sh"
