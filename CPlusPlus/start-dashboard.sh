#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BINARY="$SCRIPT_DIR/build/wmbus-gateway"

if [ ! -x "$BINARY" ]; then
    echo "Binary not found: $BINARY"
    echo "Build it first with: cmake -S CPlusPlus -B CPlusPlus/build && cmake --build CPlusPlus/build"
    exit 1
fi

find_serial_port() {
    local candidates=()

    if [[ "$(uname -s)" == "Darwin" ]]; then
        while IFS= read -r port; do
            candidates+=("$port")
        done < <(ls /dev/cu.* 2>/dev/null | grep -E 'usb|serial|wch|SLAB|ACM|USB' || true)
    else
        while IFS= read -r port; do
            candidates+=("$port")
        done < <(ls /dev/ttyUSB* /dev/ttyACM* 2>/dev/null || true)
    fi

    if [ "${#candidates[@]}" -eq 0 ]; then
        return 1
    fi

    printf '%s\n' "${candidates[0]}"
}

PORT="${METIS_PORT:-}"
if [ -z "$PORT" ]; then
    PORT="$(find_serial_port || true)"
fi

if [ -z "$PORT" ]; then
    echo "No Metis USB serial device found."
    echo "Set it explicitly with: METIS_PORT=/dev/ttyUSB0 $0"
    exit 1
fi

MQTT_HOST="${MQTT_HOST:-dashboard.yrki.net}"
MQTT_PORT="${MQTT_PORT:-1883}"
MQTT_TOPIC="${MQTT_TOPIC:-wmbus/raw}"

echo "Starting wmbus-gateway"
echo "  port:  $PORT"
echo "  mqtt:  $MQTT_HOST:$MQTT_PORT"
echo "  topic: $MQTT_TOPIC"

exec "$BINARY" \
    --port "$PORT" \
    --mqtt-host "$MQTT_HOST" \
    --mqtt-port "$MQTT_PORT" \
    --topic "$MQTT_TOPIC" \
    "$@"
