#pragma once

#include "metis_protocol.h"

#include <optional>
#include <string>

struct ServerPayload {
    std::string payload_hex;
    std::string gateway_id;
    std::optional<int> rssi;
    std::string timestamp_utc;
    std::string topic;
};

std::optional<ServerPayload> wmbus_parse_and_print(
    const MetisFrame& frame, bool rssi_enabled,
    const std::string& gateway_id, const std::string& topic);
