#include "wmbus_parser.h"
#include "hex.h"
#include "log.h"

#include <chrono>
#include <ctime>
#include <optional>

static std::string timestamp_utc_iso() {
    using namespace std::chrono;
    auto now = system_clock::now();
    auto ms = duration_cast<milliseconds>(now.time_since_epoch()) % 1000;
    auto time = system_clock::to_time_t(now);
    struct tm tm_buf;
    gmtime_r(&time, &tm_buf);

    char buf[64];
    auto len = strftime(buf, sizeof(buf), "%Y-%m-%dT%H:%M:%S", &tm_buf);
    snprintf(buf + len, sizeof(buf) - len, ".%03d0000+00:00", static_cast<int>(ms.count()));
    return buf;
}

static std::optional<std::string> decode_manufacturer(uint16_t code) {
    char a = static_cast<char>(((code >> 10) & 0x1F) + 64);
    char b = static_cast<char>(((code >>  5) & 0x1F) + 64);
    char c = static_cast<char>((code & 0x1F) + 64);
    if (a >= 'A' && a <= 'Z' && b >= 'A' && b <= 'Z' && c >= 'A' && c <= 'Z') {
        return std::string{a, b, c};
    }
    return std::nullopt;
}

static std::optional<std::string> decode_bcd(const uint8_t* data, size_t len) {
    std::string result;
    result.reserve(len * 2);
    for (int i = static_cast<int>(len) - 1; i >= 0; --i) {
        int hi = (data[i] >> 4) & 0x0F;
        int lo = data[i] & 0x0F;
        if (hi > 9 || lo > 9) return std::nullopt;
        result += static_cast<char>('0' + hi);
        result += static_cast<char>('0' + lo);
    }
    return result;
}

std::optional<ServerPayload> wmbus_parse_and_print(
    const MetisFrame& frame, bool rssi_enabled,
    const std::string& gateway_id, const std::string& topic)
{
    const auto& payload = frame.payload;

    if (payload.size() < 11) {
        LOG_INFO("CMD_DATA_IND (%zuB): %s", payload.size(), to_hex(payload).c_str());
        return std::nullopt;
    }

    std::optional<int> rssi_dbm;
    const uint8_t* wmbus;
    size_t wmbus_len;

    if (rssi_enabled && payload.size() >= 12) {
        uint8_t rssi = payload.back();
        rssi_dbm = (rssi >= 128 ? (rssi - 256) : rssi) / 2 - 74;
        wmbus = payload.data();
        wmbus_len = payload.size() - 1;
    } else {
        wmbus = payload.data();
        wmbus_len = payload.size();
    }

    uint8_t l_field = wmbus[0];
    uint8_t c_field = wmbus[1];
    uint16_t mfr = wmbus[2] | (wmbus[3] << 8);
    auto mfr_str = decode_manufacturer(mfr);
    auto id = decode_bcd(wmbus + 4, 4);
    uint8_t version = wmbus[8];
    uint8_t device_type = wmbus[9];

    std::string rssi_str;
    if (rssi_dbm.has_value()) {
        char buf[32];
        snprintf(buf, sizeof(buf), " RSSI=%ddBm", rssi_dbm.value());
        rssi_str = buf;
    }

    LOG_INFO("WMBus L=%d C=0x%02X Mfr=%s Id=%s Ver=0x%02X Dev=0x%02X%s",
        l_field, c_field,
        mfr_str.value_or("???").c_str(),
        id.value_or("????????").c_str(),
        version, device_type, rssi_str.c_str());

    if (wmbus_len > 10) {
        uint8_t ci_field = wmbus[10];
        LOG_INFO("CI=0x%02X AppData(%zuB): %s",
            ci_field, wmbus_len - 11, to_hex(wmbus + 11, wmbus_len - 11).c_str());
    }

    auto wmbus_hex = to_hex(wmbus, wmbus_len);
    LOG_INFO("RAW(%zuB): %s", wmbus_len, wmbus_hex.c_str());

    return ServerPayload{
        wmbus_hex,
        gateway_id,
        rssi_dbm,
        timestamp_utc_iso(),
        topic
    };
}
