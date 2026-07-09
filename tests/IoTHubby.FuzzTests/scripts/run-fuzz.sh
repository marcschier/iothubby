#!/usr/bin/env bash
# Copyright (c) marcschier. Licensed under the MIT License.
#
# Run a SharpFuzz harness via libfuzzer-dotnet.
#
# Usage:
#   ./scripts/run-fuzz.sh <harness> [max_total_time_seconds]
#
#   harness                = topics | codec | propertybag | connstring | twinjson | dpsresponse
#   max_total_time_seconds = libFuzzer -max_total_time (default 30)
#
# Downloads a pinned libfuzzer-dotnet on first run, instruments IoTHubby.dll with the SharpFuzz
# dotnet tool, then drives the harness.
set -euo pipefail

HARNESS="${1:-topics}"
MAX_TIME="${2:-30}"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJ_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
TOOLS_DIR="$PROJ_DIR/.tools"
CORPUS="$PROJ_DIR/corpus/$HARNESS"
FINDINGS="$PROJ_DIR/findings/$HARNESS"
mkdir -p "$TOOLS_DIR" "$CORPUS" "$FINDINGS"

# 1. Build (Release) and instrument IoTHubby.dll with SharpFuzz.
echo "==> Restoring + building IoTHubby.FuzzTests (Release)..."
dotnet build "$PROJ_DIR/IoTHubby.FuzzTests.csproj" -c Release -nologo -v quiet >/dev/null
OUT_DIR="$PROJ_DIR/bin/Release/net10.0"
TARGET_DLL="$OUT_DIR/IoTHubby.dll"

if ! command -v sharpfuzz >/dev/null 2>&1; then
  echo "==> Installing SharpFuzz.CommandLine global tool..."
  dotnet tool install --global SharpFuzz.CommandLine >/dev/null
  export PATH="$PATH:$HOME/.dotnet/tools"
fi

echo "==> Instrumenting $(basename "$TARGET_DLL")..."
sharpfuzz "$TARGET_DLL" >/dev/null

# 2. Ensure libfuzzer-dotnet driver exists.
LIBFUZZER="$TOOLS_DIR/libfuzzer-dotnet"
if [ ! -x "$LIBFUZZER" ]; then
  echo "==> Downloading libfuzzer-dotnet driver (pinned)..."
  URL="https://github.com/Metalnem/libfuzzer-dotnet/releases/download/v2024.08.10.0821/libfuzzer-dotnet-ubuntu"
  curl -fsSL "$URL" -o "$LIBFUZZER"
  chmod +x "$LIBFUZZER"
fi

# 3. Run.
echo "==> Running $HARNESS for $MAX_TIME seconds..."
FUZZ_HARNESS="$HARNESS" "$LIBFUZZER" \
  --target_path="$OUT_DIR/IoTHubby.FuzzTests" \
  --target_arg="$HARNESS" \
  -max_total_time="$MAX_TIME" \
  -artifact_prefix="$FINDINGS/" \
  "$CORPUS"
