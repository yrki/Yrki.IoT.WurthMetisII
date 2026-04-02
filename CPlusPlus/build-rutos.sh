#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/build-rutos"

if [ -z "${RUTOS_TOOLCHAIN_DIR:-}" ] || [ -z "${RUTOS_TARGET_DIR:-}" ]; then
    echo "Error: Set RUTOS_TOOLCHAIN_DIR and RUTOS_TARGET_DIR environment variables."
    echo ""
    echo "Example:"
    echo "  export RUTOS_TOOLCHAIN_DIR=/opt/rutos-sdk/staging_dir/toolchain-arm_cortex-a7+neon-vfpv4_gcc-12.3.0_musl_eabi"
    echo "  export RUTOS_TARGET_DIR=/opt/rutos-sdk/staging_dir/target-arm_cortex-a7+neon-vfpv4_musl_eabi"
    exit 1
fi

echo "Building wmbus-gateway for RUTOS (ARM Cortex-A7)..."
cmake -S "$SCRIPT_DIR" -B "$BUILD_DIR" \
    -DCMAKE_TOOLCHAIN_FILE="$SCRIPT_DIR/toolchain-rutos.cmake" \
    -DCMAKE_BUILD_TYPE=Release

cmake --build "$BUILD_DIR" --parallel

echo "Build complete: $BUILD_DIR/wmbus-gateway"
