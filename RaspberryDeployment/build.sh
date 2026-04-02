#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
OUTPUT_DIR="$SCRIPT_DIR/publish"

echo "Building single-file for linux-arm64..."
dotnet publish "$PROJECT_DIR/Yrki.IoT.WurthMetisII.csproj" \
    --runtime linux-arm64 \
    --configuration Release \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    --output "$OUTPUT_DIR"

echo "Build complete: $OUTPUT_DIR/Yrki.IoT.WurthMetisII"
