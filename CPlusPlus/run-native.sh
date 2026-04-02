#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BINARY="$SCRIPT_DIR/build-native/wmbus-gateway"

if [ ! -x "$BINARY" ]; then
    echo "Binary not found: $BINARY"
    echo "Run ./build-native.sh first."
    exit 1
fi

exec "$BINARY" "$@"
