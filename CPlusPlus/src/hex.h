#pragma once

#include <cstdint>
#include <string>
#include <vector>

inline std::string to_hex(const uint8_t* data, size_t len) {
    static const char digits[] = "0123456789ABCDEF";
    std::string result;
    result.reserve(len * 2);
    for (size_t i = 0; i < len; ++i) {
        result += digits[(data[i] >> 4) & 0x0F];
        result += digits[data[i] & 0x0F];
    }
    return result;
}

inline std::string to_hex(const std::vector<uint8_t>& data) {
    return to_hex(data.data(), data.size());
}
