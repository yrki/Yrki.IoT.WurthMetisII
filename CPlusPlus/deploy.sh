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
echo -e "  ${DIM}WMBus Gateway Deployer — RUTOS (C++)${NC}"
echo -e "  ${DIM}─────────────────────────────────────${NC}"
echo ""

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/config.env"

BINARY="$SCRIPT_DIR/build-rutos/wmbus-gateway"
REMOTE_DIR="/opt/wmbus-gateway"
SERVICE_NAME="wmbus-gateway"

if [ ! -f "$BINARY" ]; then
    echo -e "  ${RED}Binary not found. Run ./build-rutos.sh first.${NC}"
    exit 1
fi

# Prompt for configuration (defaults from config.env)
echo -e "  ${BOLD}${MAGENTA}RUTOS Device${NC}"
echo -e "  ${DIM}────────────${NC}"
read -rp $'  \033[0;36mIP/hostname\033[0m [\033[2m'"${DEVICE_HOST}"$'\033[0m]: ' input
DEVICE_HOST="${input:-$DEVICE_HOST}"

read -rp $'  \033[0;36mUsername\033[0m [\033[2m'"${DEVICE_USER}"$'\033[0m]: ' input
DEVICE_USER="${input:-$DEVICE_USER}"

read -rsp $'  \033[0;36mPassword\033[0m [\033[2mempty = SSH key\033[0m]: ' DEVICE_PASS
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
if [ -n "$DEVICE_PASS" ]; then
    if ! command -v sshpass &> /dev/null; then
        echo -e "\n  ${RED}Error: sshpass is required for password auth. Install with: brew install sshpass${NC}"
        exit 1
    fi
    SSH="sshpass -p $DEVICE_PASS ssh"
    SCP="sshpass -p $DEVICE_PASS scp"
else
    SSH="ssh"
    SCP="scp"
fi

echo ""
echo -e "  ${DIM}─────────────────────────────────────${NC}"
echo -e "  ${BOLD}${BLUE}Deploying to ${CYAN}${DEVICE_USER}@${DEVICE_HOST}${NC}"
echo ""

# Create remote directory and copy binary
echo -e "  ${YELLOW}[1/4]${NC} Creating remote directory..."
$SSH "$DEVICE_USER@$DEVICE_HOST" "mkdir -p $REMOTE_DIR"
echo -e "  ${GREEN}  done${NC}"

echo -e "  ${YELLOW}[2/4]${NC} Copying binary..."
$SCP "$BINARY" "$DEVICE_USER@$DEVICE_HOST:$REMOTE_DIR/"
$SSH "$DEVICE_USER@$DEVICE_HOST" "chmod +x $REMOTE_DIR/wmbus-gateway"
echo -e "  ${GREEN}  done${NC}"

# Build argument list
ARGS="--port $SERIAL_PORT --baud $BAUD_RATE --mqtt-host $MQTT_HOST --mqtt-port $MQTT_PORT --topic $MQTT_TOPIC"
if [ -n "${GATEWAY_ID:-}" ]; then
    ARGS="$ARGS --gateway-id $GATEWAY_ID"
fi
if [ "${ACTIVATE:-false}" = "true" ]; then
    ARGS="$ARGS --activate"
fi

# Generate and install procd init script (RUTOS/OpenWrt uses procd, not systemd)
echo -e "  ${YELLOW}[3/4]${NC} Installing init script..."
cat <<EOF | $SSH "$DEVICE_USER@$DEVICE_HOST" "cat > /etc/init.d/${SERVICE_NAME} && chmod +x /etc/init.d/${SERVICE_NAME}"
#!/bin/sh /etc/rc.common

START=99
STOP=10
USE_PROCD=1

start_service() {
    procd_open_instance
    procd_set_param command ${REMOTE_DIR}/wmbus-gateway ${ARGS}
    procd_set_param respawn 3600 5 0
    procd_set_param stdout 1
    procd_set_param stderr 1
    procd_close_instance
}
EOF
echo -e "  ${GREEN}  done${NC}"

# Enable and start
echo -e "  ${YELLOW}[4/4]${NC} Starting service..."
$SSH "$DEVICE_USER@$DEVICE_HOST" "/etc/init.d/${SERVICE_NAME} enable && /etc/init.d/${SERVICE_NAME} restart"
echo -e "  ${GREEN}  done${NC}"

echo ""
echo -e "  ${DIM}─────────────────────────────────────${NC}"
echo -e "  ${GREEN}${BOLD}Deployment complete!${NC}"
echo ""
echo -e "  ${DIM}Status:${NC}  ssh $DEVICE_USER@$DEVICE_HOST /etc/init.d/$SERVICE_NAME status"
echo -e "  ${DIM}Logs:${NC}    ssh $DEVICE_USER@$DEVICE_HOST logread -f"
echo ""
