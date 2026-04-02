#include "mqtt_client.h"
#include "log.h"

#include <chrono>
#include <mosquitto.h>

static int64_t now_ms() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}

MqttClient::MqttClient() {
    mosquitto_lib_init();
    m_mosq = mosquitto_new(nullptr, true, nullptr);
}

MqttClient::~MqttClient() {
    if (m_mosq) {
        if (m_connected) mosquitto_disconnect(m_mosq);
        mosquitto_destroy(m_mosq);
    }
    mosquitto_lib_cleanup();
}

void MqttClient::start(const std::string& host, int port) {
    m_host = host;
    m_port = port;
    ensure_connected("connected");
}

void MqttClient::send(const ServerPayload& payload) {
    if (!ensure_connected("reconnected")) return;

    std::string rssi_str = payload.rssi.has_value()
        ? std::to_string(payload.rssi.value())
        : "null";

    std::string json = "{\"payloadHex\":\"" + payload.payload_hex
        + "\",\"gatewayId\":\"" + payload.gateway_id
        + "\",\"rssi\":" + rssi_str
        + ",\"timestamp\":\"" + payload.timestamp_utc + "\"}";

    int rc = mosquitto_publish(m_mosq, nullptr, payload.topic.c_str(),
        static_cast<int>(json.size()), json.c_str(), 0, false);

    if (rc != MOSQ_ERR_SUCCESS) {
        LOG_WARN("MQTT publish failed: %s", mosquitto_strerror(rc));
        m_connected = false;
    }
}

bool MqttClient::ensure_connected(const char* verb) {
    if (m_connected) return true;
    if (now_ms() < m_next_reconnect_ms) return false;

    int rc = mosquitto_connect(m_mosq, m_host.c_str(), m_port, 60);
    m_next_reconnect_ms = now_ms() + 5000;

    if (rc == MOSQ_ERR_SUCCESS) {
        m_connected = true;
        LOG_INFO("MQTT %s to %s:%d", verb, m_host.c_str(), m_port);
        return true;
    }

    LOG_WARN("MQTT connection to %s:%d failed: %s", m_host.c_str(), m_port, mosquitto_strerror(rc));
    LOG_INFO("Continuing without MQTT");
    return false;
}
