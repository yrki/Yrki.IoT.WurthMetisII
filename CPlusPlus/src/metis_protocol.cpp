#include "metis_protocol.h"
#include "hex.h"
#include "log.h"

#include <chrono>
#include <cstring>
#include <stdexcept>
#include <thread>
#include <unistd.h>

std::vector<uint8_t> metis_build_frame(uint8_t command, const std::vector<uint8_t>& payload) {
    std::vector<uint8_t> frame(payload.size() + 4);
    frame[0] = 0xFF;
    frame[1] = command;
    frame[2] = static_cast<uint8_t>(payload.size());
    std::copy(payload.begin(), payload.end(), frame.begin() + 3);

    uint8_t checksum = 0x00;
    for (size_t i = 0; i < frame.size() - 1; ++i) {
        checksum ^= frame[i];
    }
    frame.back() = checksum;
    return frame;
}

bool metis_try_extract_frame(std::vector<uint8_t>& buffer, MetisFrame& frame) {
    while (!buffer.empty()) {
        if (buffer[0] != 0xFF) {
            buffer.erase(buffer.begin());
            continue;
        }

        if (buffer.size() < 4) return false;

        uint8_t payload_length = buffer[2];
        size_t total_length = payload_length + 4;
        if (buffer.size() < total_length) return false;

        std::vector<uint8_t> raw(buffer.begin(), buffer.begin() + total_length);
        buffer.erase(buffer.begin(), buffer.begin() + total_length);

        uint8_t checksum = 0x00;
        for (size_t i = 0; i < raw.size() - 1; ++i) {
            checksum ^= raw[i];
        }

        if (checksum != raw.back()) continue;

        frame.command = raw[1];
        frame.payload.assign(raw.begin() + 3, raw.begin() + 3 + payload_length);
        frame.raw_frame = std::move(raw);
        return true;
    }
    return false;
}

static int64_t tick_ms() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}

static MetisFrame wait_for_frame(int fd, std::vector<uint8_t>& buffer,
    uint8_t expected_command, int timeout_ms)
{
    int64_t started = tick_ms();
    uint8_t buf[512];

    while (tick_ms() - started < timeout_ms) {
        ssize_t n = read(fd, buf, sizeof(buf));
        if (n <= 0) {
            std::this_thread::sleep_for(std::chrono::milliseconds(25));
            continue;
        }

        LOG_DEBUG("PAYLOAD: %s", to_hex(buf, n).c_str());
        buffer.insert(buffer.end(), buf, buf + n);

        MetisFrame frame;
        while (metis_try_extract_frame(buffer, frame)) {
            LOG_INFO("METIS CMD=0x%02X LEN=%zu HEX=%s",
                frame.command, frame.payload.size(), to_hex(frame.raw_frame).c_str());
            if (frame.command == expected_command) {
                return frame;
            }
        }
    }

    throw std::runtime_error("Timed out waiting for 0x" + to_hex(&expected_command, 1));
}

static void drain_during_pause(int fd, std::vector<uint8_t>& buffer, int duration_ms) {
    int64_t started = tick_ms();
    uint8_t buf[512];

    while (tick_ms() - started < duration_ms) {
        ssize_t n = read(fd, buf, sizeof(buf));
        if (n <= 0) {
            std::this_thread::sleep_for(std::chrono::milliseconds(25));
            continue;
        }

        LOG_DEBUG("PAYLOAD: %s", to_hex(buf, n).c_str());
        buffer.insert(buffer.end(), buf, buf + n);

        MetisFrame frame;
        while (metis_try_extract_frame(buffer, frame)) {
            LOG_INFO("METIS CMD=0x%02X LEN=%zu HEX=%s",
                frame.command, frame.payload.size(), to_hex(frame.raw_frame).c_str());
        }
    }
}

void metis_send_and_wait(int fd, std::vector<uint8_t>& buffer,
    const std::string& label, const std::vector<uint8_t>& command,
    uint8_t expected_command, int timeout_ms, int attempts)
{
    std::string last_error;

    for (int attempt = 1; attempt <= attempts; ++attempt) {
        LOG_INFO("TX %s attempt %d/%d: %s", label.c_str(), attempt, attempts, to_hex(command).c_str());
        write(fd, command.data(), command.size());

        try {
            auto response = wait_for_frame(fd, buffer, expected_command, timeout_ms);
            if (!response.payload.empty() && response.payload[0] != 0x00) {
                char msg[128];
                snprintf(msg, sizeof(msg), "%s failed with status 0x%02X", label.c_str(), response.payload[0]);
                throw std::runtime_error(msg);
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(80));
            return;
        } catch (const std::exception& ex) {
            last_error = ex.what();
            drain_during_pause(fd, buffer, 250);
            std::this_thread::sleep_for(std::chrono::milliseconds(120));
        }
    }

    throw std::runtime_error(last_error);
}

MetisFrame metis_send_and_get(int fd, std::vector<uint8_t>& buffer,
    const std::string& label, const std::vector<uint8_t>& command,
    uint8_t expected_command, int timeout_ms)
{
    LOG_INFO("TX %s: %s", label.c_str(), to_hex(command).c_str());
    write(fd, command.data(), command.size());
    return wait_for_frame(fd, buffer, expected_command, timeout_ms);
}

void metis_send_and_pause(int fd, std::vector<uint8_t>& buffer,
    const std::string& label, const std::vector<uint8_t>& command,
    int pause_ms)
{
    LOG_INFO("TX %s: %s", label.c_str(), to_hex(command).c_str());
    write(fd, command.data(), command.size());
    drain_during_pause(fd, buffer, pause_ms);
}
