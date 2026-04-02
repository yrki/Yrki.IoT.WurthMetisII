#pragma once

#include <string>

enum class LogLevel { Debug, Info, Warning, Error };

void log_set_level(LogLevel level);
void log_msg(LogLevel level, const char* fmt, ...);

#define LOG_DEBUG(...) log_msg(LogLevel::Debug, __VA_ARGS__)
#define LOG_INFO(...)  log_msg(LogLevel::Info, __VA_ARGS__)
#define LOG_WARN(...)  log_msg(LogLevel::Warning, __VA_ARGS__)
#define LOG_ERROR(...) log_msg(LogLevel::Error, __VA_ARGS__)
