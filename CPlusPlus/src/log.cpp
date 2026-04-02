#include "log.h"

#include <chrono>
#include <cstdarg>
#include <cstdio>
#include <ctime>

static LogLevel g_min_level = LogLevel::Info;

void log_set_level(LogLevel level) {
    g_min_level = level;
}

static std::string timestamp_now() {
    using namespace std::chrono;
    auto now = system_clock::now();
    auto ms = duration_cast<milliseconds>(now.time_since_epoch()) % 1000;
    auto time = system_clock::to_time_t(now);
    struct tm tm_buf;
    localtime_r(&time, &tm_buf);

    char buf[64];
    auto len = strftime(buf, sizeof(buf), "%Y-%m-%d %H:%M:%S", &tm_buf);
    snprintf(buf + len, sizeof(buf) - len, ".%03d", static_cast<int>(ms.count()));
    return buf;
}

void log_msg(LogLevel level, const char* fmt, ...) {
    if (level < g_min_level) return;

    const char* prefix = "info";
    switch (level) {
        case LogLevel::Debug:   prefix = "dbug"; break;
        case LogLevel::Info:    prefix = "info"; break;
        case LogLevel::Warning: prefix = "warn"; break;
        case LogLevel::Error:   prefix = "fail"; break;
    }

    auto ts = timestamp_now();
    fprintf(stderr, "%s %s: ", ts.c_str(), prefix);

    va_list args;
    va_start(args, fmt);
    vfprintf(stderr, fmt, args);
    va_end(args);

    fprintf(stderr, "\n");
    fflush(stderr);
}
