#!/bin/bash
set -euo pipefail

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
BOLD='\033[1m'
DIM='\033[2m'
NC='\033[0m'

# Banner
echo -e "${CYAN}"
cat << 'BANNER'

 __   __       _    _     ___      _____
 \ \ / /      | |  (_)   |_ _|    |_   _|
  \ V /  _ __ | | ___     | |  ___  | |
   | |  | '__|| |/ / |    | | / _ \ | |
   | |  | |   |   <| | _ _| || (_) || |
   |_|  |_|   |_|\_\_|(_)___/ \___/ |_|

BANNER
echo -e "${NC}"
echo -e "  ${DIM}WMBus Gateway Deployer — Wurth Metis-II${NC}"
echo -e "  ${DIM}────────────────────────────────────────${NC}"
echo ""

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/config.env"

BINARY="$SCRIPT_DIR/publish/Yrki.IoT.WurthMetisII"
REMOTE_DIR="/opt/wmbus-gateway"
SERVICE_NAME="wmbus-gateway"

if [ ! -f "$BINARY" ]; then
    echo -e "  ${RED}Binary not found. Run ./build.sh first.${NC}"
    exit 1
fi

# Prompt for configuration (defaults from config.env)
echo -e "  ${BOLD}${MAGENTA}Raspberry Pi${NC}"
echo -e "  ${DIM}─────────────${NC}"
read -rp $'  \033[0;36mIP/hostname\033[0m [\033[2m'"${PI_HOST}"$'\033[0m]: ' input
PI_HOST="${input:-$PI_HOST}"

read -rp $'  \033[0;36mUsername\033[0m [\033[2m'"${PI_USER}"$'\033[0m]: ' input
PI_USER="${input:-$PI_USER}"

read -rsp $'  \033[0;36mPassword\033[0m [\033[2mempty = SSH key\033[0m]: ' PI_PASS
echo

echo ""
echo -e "  ${BOLD}${MAGENTA}Gateway${NC}"
echo -e "  ${DIM}───────${NC}"
read -rp $'  \033[0;36mGateway ID\033[0m [\033[2mhostname\033[0m]: ' input
GATEWAY_ID="${input:-${GATEWAY_ID:-}}"

read -rp $'  \033[0;36mSerial port\033[0m [\033[2m'"${SERIAL_PORT}"$'\033[0m]: ' input
SERIAL_PORT="${input:-$SERIAL_PORT}"

echo ""
echo -e "  ${BOLD}${MAGENTA}MQTT${NC}"
echo -e "  ${DIM}────${NC}"
read -rp $'  \033[0;36mHost\033[0m [\033[2m'"${MQTT_HOST}"$'\033[0m]: ' input
MQTT_HOST="${input:-$MQTT_HOST}"

read -rp $'  \033[0;36mPort\033[0m [\033[2m'"${MQTT_PORT}"$'\033[0m]: ' input
MQTT_PORT="${input:-$MQTT_PORT}"

read -rp $'  \033[0;36mTopic\033[0m [\033[2m'"${MQTT_TOPIC}"$'\033[0m]: ' input
MQTT_TOPIC="${input:-$MQTT_TOPIC}"

# Build SSH/SCP command with password support via sshpass if needed
if [ -n "$PI_PASS" ]; then
    if ! command -v sshpass &> /dev/null; then
        echo -e "\n  ${RED}Error: sshpass is required for password auth. Install with: brew install sshpass${NC}"
        exit 1
    fi
    SSH="sshpass -p $PI_PASS ssh"
    SCP="sshpass -p $PI_PASS scp"
else
    SSH="ssh"
    SCP="scp"
fi

echo ""
echo -e "  ${DIM}────────────────────────────────────────${NC}"
echo -e "  ${BOLD}${BLUE}Deploying to ${CYAN}${PI_USER}@${PI_HOST}${NC}"
echo ""

# Create remote directory and copy binary
echo -e "  ${YELLOW}[1/4]${NC} Creating remote directory..."
$SSH "$PI_USER@$PI_HOST" "sudo mkdir -p $REMOTE_DIR && sudo chown $PI_USER:$PI_USER $REMOTE_DIR"
echo -e "  ${GREEN}  done${NC}"

echo -e "  ${YELLOW}[2/4]${NC} Copying binary..."
$SCP "$BINARY" "$PI_USER@$PI_HOST:$REMOTE_DIR/"
$SSH "$PI_USER@$PI_HOST" "chmod +x $REMOTE_DIR/Yrki.IoT.WurthMetisII"
echo -e "  ${GREEN}  done${NC}"

# Build argument list
ARGS="--port $SERIAL_PORT --baud $BAUD_RATE --mqtt-host $MQTT_HOST --mqtt-port $MQTT_PORT --topic $MQTT_TOPIC --log-file $REMOTE_DIR/payloads.log"
if [ -n "${GATEWAY_ID:-}" ]; then
    ARGS="$ARGS --gateway-id $GATEWAY_ID"
fi
if [ "${ACTIVATE:-false}" = "true" ]; then
    ARGS="$ARGS --activate"
fi

# Generate and install systemd service
echo -e "  ${YELLOW}[3/4]${NC} Installing systemd service..."
cat <<EOF | $SSH "$PI_USER@$PI_HOST" "sudo tee /etc/systemd/system/${SERVICE_NAME}.service > /dev/null"
[Unit]
Description=WMBus Gateway (Wurth Metis-II)
After=network.target

[Service]
Type=simple
ExecStart=${REMOTE_DIR}/Yrki.IoT.WurthMetisII ${ARGS}
WorkingDirectory=${REMOTE_DIR}
Restart=always
RestartSec=5
User=${PI_USER}
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF
echo -e "  ${GREEN}  done${NC}"

# Enable and start
echo -e "  ${YELLOW}[4/4]${NC} Starting service..."
$SSH "$PI_USER@$PI_HOST" "sudo systemctl daemon-reload && sudo systemctl enable ${SERVICE_NAME} && sudo systemctl restart ${SERVICE_NAME}"
echo -e "  ${GREEN}  done${NC}"

echo ""
echo -e "  ${DIM}────────────────────────────────────────${NC}"
echo -e "  ${GREEN}${BOLD}Deployment complete!${NC}"
echo ""
echo -e "  ${DIM}Status:${NC}  ssh $PI_USER@$PI_HOST sudo systemctl status $SERVICE_NAME"
echo -e "  ${DIM}Logs:${NC}    ssh $PI_USER@$PI_HOST sudo journalctl -u $SERVICE_NAME -f"
echo ""
