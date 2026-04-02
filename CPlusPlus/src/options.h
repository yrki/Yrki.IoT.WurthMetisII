#pragma once

#include <string>
#include <unistd.h>
#include <climits>

#ifndef HOST_NAME_MAX
#define HOST_NAME_MAX 255
#endif

struct RuntimeOptions {
#if defined(__APPLE__)
    std::string port_name   = "/dev/cu.usbserial-53002FA7";
#else
    std::string port_name   = "/dev/ttyUSB0";
#endif
    int         baud_rate   = 9600;
    bool        activate    = false;
    std::string gateway_id;
    std::string mqtt_host   = "localhost";
    int         mqtt_port   = 1883;
    std::string mqtt_topic  = "wmbus/raw";
    bool        dump_params = false;
    bool        show_help   = false;

    static RuntimeOptions parse(int argc, char* argv[]) {
        RuntimeOptions opts;

        char hostname[HOST_NAME_MAX + 1] = {};
        if (gethostname(hostname, sizeof(hostname)) == 0) {
            opts.gateway_id = hostname;
        } else {
            opts.gateway_id = "unknown";
        }

        for (int i = 1; i < argc; ++i) {
            std::string arg = argv[i];
            if (arg == "--port" && i + 1 < argc)        opts.port_name = argv[++i];
            else if (arg == "--baud" && i + 1 < argc)   opts.baud_rate = std::stoi(argv[++i]);
            else if (arg == "--activate")                opts.activate = true;
            else if (arg == "--gateway-id" && i + 1 < argc) opts.gateway_id = argv[++i];
            else if (arg == "--mqtt-host" && i + 1 < argc)  opts.mqtt_host = argv[++i];
            else if (arg == "--mqtt-port" && i + 1 < argc)  opts.mqtt_port = std::stoi(argv[++i]);
            else if (arg == "--topic" && i + 1 < argc)      opts.mqtt_topic = argv[++i];
            else if (arg == "--dump-params")             opts.dump_params = true;
            else if (arg == "--help" || arg == "-h")     opts.show_help = true;
        }
        return opts;
    }

    static void print_usage() {
        fprintf(stderr,
#if defined(__APPLE__)
            "Usage: wmbus-gateway [--port /dev/cu.usbserial-53002FA7] [--baud 9600] [--activate] "
#else
            "Usage: wmbus-gateway [--port /dev/ttyUSB0] [--baud 9600] [--activate] "
#endif
            "[--dump-params] [--gateway-id <name>] "
            "[--mqtt-host localhost] [--mqtt-port 1883] [--topic wmbus/raw]\n");
    }
};
