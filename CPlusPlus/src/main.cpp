#include "hex.h"
#include "log.h"
#include "metis_protocol.h"
#include "mqtt_client.h"
#include "options.h"
#include "serial_port.h"
#include "wmbus_parser.h"

#include <atomic>
#include <csignal>
#include <cstring>
#include <thread>
#include <unistd.h>

static std::atomic<bool> g_running{true};

static void signal_handler(int) {
    g_running = false;
}

static void activate(const RuntimeOptions& opts) {
    LOG_INFO("Activating Metis-II Plug with minimal collector setup");

    int fd = serial_open(opts.port_name, opts.baud_rate);
    std::vector<uint8_t> buffer;
    buffer.reserve(1024);

    metis_send_and_wait(fd, buffer, "CMD_SET_REQ UART_CMD_OUT_ENABLE=1",
        metis_build_frame(0x09, {0x05, 0x01, 0x01}), 0x89);
    metis_send_and_wait(fd, buffer, "CMD_SET_REQ RSSI_ENABLE=1",
        metis_build_frame(0x09, {0x45, 0x01, 0x01}), 0x89);
    metis_send_and_wait(fd, buffer, "CMD_SET_REQ MODE_PRESELECT=C2_T2_other",
        metis_build_frame(0x09, {0x46, 0x01, 0x09}), 0x89);
    metis_send_and_pause(fd, buffer, "CMD_RESET_REQ",
        metis_build_frame(0x05, {}), 1200);

    close(fd);

    int fd2 = serial_open(opts.port_name, opts.baud_rate);
    std::vector<uint8_t> buffer2;
    buffer2.reserve(1024);
    metis_send_and_wait(fd2, buffer2, "CMD_SET_MODE_REQ C2_T2_other",
        metis_build_frame(0x04, {0x09}), 0x84);
    close(fd2);
}

static void dump_parameters(const RuntimeOptions& opts) {
    LOG_INFO("Reading parameters 0x00..0x50");

    int fd = serial_open(opts.port_name, opts.baud_rate);
    std::vector<uint8_t> buffer;
    buffer.reserve(256);

    for (uint8_t param = 0x00; param <= 0x50 && g_running; ++param) {
        try {
            auto response = metis_send_and_get(fd, buffer,
                "GET 0x" + to_hex(&param, 1),
                metis_build_frame(0x0A, {param, 0x01}),
                0x8A, 500);

            if (response.payload.size() >= 2) {
                uint8_t len = response.payload[1];
                auto values_start = response.payload.data() + 2;
                auto values_len = response.payload.size() - 2;
                std::string dec;
                for (size_t i = 0; i < values_len; ++i) {
                    if (!dec.empty()) dec += ", ";
                    dec += std::to_string(values_start[i]);
                }
                LOG_INFO("[0x%02X] len=%d hex=%s dec=%s",
                    param, len, to_hex(values_start, values_len).c_str(), dec.c_str());
            } else {
                LOG_INFO("[0x%02X] raw=%s", param, to_hex(response.payload).c_str());
            }
        } catch (const std::runtime_error&) {
            LOG_WARN("[0x%02X] no response", param);
        }
    }

    close(fd);
}

static bool try_read_rssi_enabled(int fd) {
    std::vector<uint8_t> buffer;
    buffer.reserve(256);

    try {
        auto response = metis_send_and_get(fd, buffer,
            "CMD_GET_REQ RSSI_ENABLE",
            metis_build_frame(0x0A, {0x45, 0x01}),
            0x8A);

        bool enabled = response.payload.size() >= 3 && response.payload[2] == 0x01;
        LOG_INFO("RSSI_ENABLE = %s", enabled ? "true" : "false");
        return enabled;
    } catch (const std::runtime_error&) {
        LOG_WARN("Could not read RSSI_ENABLE, assuming disabled");
        return false;
    }
}

static void listen_loop(const RuntimeOptions& opts, MqttClient& mqtt) {
    int fd = serial_open(opts.port_name, opts.baud_rate);
    bool rssi_enabled = try_read_rssi_enabled(fd);

    uint8_t chunk_buf[4096];
    std::vector<uint8_t> metis_buffer;
    metis_buffer.reserve(8192);

    LOG_INFO("Listening for WMBus telegrams. Press Ctrl+C to stop.");

    while (g_running) {
        ssize_t n = read(fd, chunk_buf, sizeof(chunk_buf));
        if (n <= 0) {
            std::this_thread::sleep_for(std::chrono::milliseconds(25));
            continue;
        }

        LOG_DEBUG("PAYLOAD: %s", to_hex(chunk_buf, n).c_str());
        metis_buffer.insert(metis_buffer.end(), chunk_buf, chunk_buf + n);

        MetisFrame frame;
        while (metis_try_extract_frame(metis_buffer, frame)) {
            if (frame.command == 0x03) {
                auto payload = wmbus_parse_and_print(frame, rssi_enabled,
                    opts.gateway_id, opts.mqtt_topic);
                if (payload.has_value()) {
                    mqtt.send(payload.value());
                }
                continue;
            }

            LOG_INFO("METIS CMD=0x%02X LEN=%zu HEX=%s",
                frame.command, frame.payload.size(), to_hex(frame.raw_frame).c_str());
        }
    }

    close(fd);
}

int main(int argc, char* argv[]) {
    auto opts = RuntimeOptions::parse(argc, argv);

    if (opts.show_help) {
        RuntimeOptions::print_usage();
        return 0;
    }

    signal(SIGINT, signal_handler);
    signal(SIGTERM, signal_handler);

    LOG_INFO("Opening %s at %d baud", opts.port_name.c_str(), opts.baud_rate);

    if (opts.activate) {
        activate(opts);
    }

    if (opts.dump_params) {
        dump_parameters(opts);
        return 0;
    }

    MqttClient mqtt;
    mqtt.start(opts.mqtt_host, opts.mqtt_port);

    listen_loop(opts, mqtt);
    return 0;
}
