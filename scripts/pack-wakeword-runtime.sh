#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAGE="${1:-/tmp/wakeword-runtime-stage}"
OUT="${2:-$ROOT/hamburbur/WakeWord/runtime.zip}"
WIN_DIR="${VOSK_WIN_DIR:-/tmp/wakeword/vosk-win64-0.3.45}"
MODEL_DIR="${VOSK_MODEL_DIR:-/tmp/wakeword/vosk-model-small-en-us-0.15}"

rm -rf "$STAGE"
mkdir -p "$STAGE/model"
cp "$WIN_DIR/libvosk.dll" "$WIN_DIR/libstdc++-6.dll" "$WIN_DIR/libgcc_s_seh-1.dll" "$WIN_DIR/libwinpthread-1.dll" "$STAGE/"
cp -a "$MODEL_DIR/." "$STAGE/model/"
echo "vosk-small-en-us-0.15-win64-0.3.45" > "$STAGE/VERSION"
cp "$ROOT/hamburbur/WakeWord/INSTALL.txt" "$STAGE/INSTALL.txt"
mkdir -p "$(dirname "$OUT")"
rm -f "$OUT"
(cd "$STAGE" && zip -qr "$OUT" .)
echo "Wrote $OUT ($(wc -c < "$OUT") bytes)"
