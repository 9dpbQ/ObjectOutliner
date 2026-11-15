#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "== Running Object Manager regression tests =="
"$ROOT_DIR/scripts/test-objectmanager.sh" "$@"

echo "== Building Rhino 8 Mac target (net7.0) =="
RHINO_TARGET_FRAMEWORK=net7.0 \
  "$ROOT_DIR/scripts/build-objectmanager.sh" "$@"

echo "== Building Rhino 8 Windows compatibility target (net48) =="
RHINO_TARGET_FRAMEWORK=net48 \
  "$ROOT_DIR/scripts/build-objectmanager.sh" "$@"
