#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

struct MetisFrame {
    uint8_t command;
    std::vector<uint8_t> payload;
    std::vector<uint8_t> raw_frame;
};

std::vector<uint8_t> metis_build_frame(uint8_t command, const std::vector<uint8_t>& payload);

bool metis_try_extract_frame(std::vector<uint8_t>& buffer, MetisFrame& frame);

void metis_send_and_wait(int fd, std::vector<uint8_t>& buffer,
    const std::string& label, const std::vector<uint8_t>& command,
    uint8_t expected_command, int timeout_ms = 1500, int attempts = 5);

MetisFrame metis_send_and_get(int fd, std::vector<uint8_t>& buffer,
    const std::string& label, const std::vector<uint8_t>& command,
    uint8_t expected_command, int timeout_ms = 1500);

void metis_send_and_pause(int fd, std::vector<uint8_t>& buffer,
    const std::string& label, const std::vector<uint8_t>& command,
    int pause_ms);
