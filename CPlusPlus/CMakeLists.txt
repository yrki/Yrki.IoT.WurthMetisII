cmake_minimum_required(VERSION 3.16)
project(wmbus-gateway LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(Threads REQUIRED)

add_executable(wmbus-gateway
    src/main.cpp
    src/serial_port.cpp
    src/metis_protocol.cpp
    src/wmbus_parser.cpp
    src/mqtt_client.cpp
    src/log.cpp
)

set(MOSQUITTO_HINT_INCLUDE_DIRS
    /opt/homebrew/include
    /usr/local/include
)

set(MOSQUITTO_HINT_LIBRARY_DIRS
    /opt/homebrew/lib
    /usr/local/lib
)

find_path(MOSQUITTO_INCLUDE_DIR
    NAMES mosquitto.h
    HINTS ${MOSQUITTO_HINT_INCLUDE_DIRS}
)

find_library(MOSQUITTO_LIB
    NAMES mosquitto
    HINTS ${MOSQUITTO_HINT_LIBRARY_DIRS}
)

if(NOT MOSQUITTO_INCLUDE_DIR OR NOT MOSQUITTO_LIB)
    message(FATAL_ERROR
        "libmosquitto not found. Install it with one of these commands:\n"
        "  macOS (Homebrew): brew install mosquitto\n"
        "  Debian/Ubuntu:    apt install libmosquitto-dev\n"
        "  RUTOS/OpenWrt:    opkg install libmosquitto\n")
endif()

target_include_directories(wmbus-gateway PRIVATE ${MOSQUITTO_INCLUDE_DIR})
target_link_libraries(wmbus-gateway PRIVATE ${MOSQUITTO_LIB} Threads::Threads)
