#pragma once

#include "wmbus_parser.h"

#include <string>

class MqttClient {
public:
    MqttClient();
    ~MqttClient();

    void start(const std::string& host, int port);
    void send(const ServerPayload& payload);

private:
    bool ensure_connected(const char* verb);

    struct mosquitto* m_mosq = nullptr;
    std::string m_host;
    int m_port = 0;
    bool m_connected = false;
    int64_t m_next_reconnect_ms = 0;
};
