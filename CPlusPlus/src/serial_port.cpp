#include "serial_port.h"
#include "log.h"

#include <cerrno>
#include <cstring>
#include <fcntl.h>
#include <termios.h>
#include <unistd.h>
#include <stdexcept>

static speed_t to_speed(int baud) {
    switch (baud) {
        case 1200:   return B1200;
        case 2400:   return B2400;
        case 4800:   return B4800;
        case 9600:   return B9600;
        case 19200:  return B19200;
        case 38400:  return B38400;
        case 57600:  return B57600;
        case 115200: return B115200;
        default:
            throw std::runtime_error("Unsupported baud rate: " + std::to_string(baud));
    }
}

int serial_open(const std::string& port_name, int baud_rate) {
    int fd = open(port_name.c_str(), O_RDWR | O_NOCTTY | O_NONBLOCK);
    if (fd < 0) {
        throw std::runtime_error("Failed to open " + port_name + ": " + strerror(errno));
    }

    // Clear non-blocking after open
    int flags = fcntl(fd, F_GETFL, 0);
    fcntl(fd, F_SETFL, flags & ~O_NONBLOCK);

    struct termios tty = {};
    if (tcgetattr(fd, &tty) != 0) {
        close(fd);
        throw std::runtime_error("tcgetattr failed: " + std::string(strerror(errno)));
    }

    speed_t speed = to_speed(baud_rate);
    cfsetispeed(&tty, speed);
    cfsetospeed(&tty, speed);

    // Raw mode: 8N1, no flow control (matches stty: cs8 -cstopb -parenb raw -ixon -ixoff)
    cfmakeraw(&tty);
    tty.c_cflag |= (CLOCAL | CREAD);
    tty.c_cflag &= ~(PARENB | CSTOPB | CRTSCTS);
    tty.c_cflag &= ~CSIZE;
    tty.c_cflag |= CS8;
    tty.c_iflag &= ~(IXON | IXOFF | IXANY);

    // VMIN=0 VTIME=1 (100ms timeout per read, matches stty: min 0 time 1)
    tty.c_cc[VMIN]  = 0;
    tty.c_cc[VTIME] = 1;

    if (tcsetattr(fd, TCSANOW, &tty) != 0) {
        close(fd);
        throw std::runtime_error("tcsetattr failed: " + std::string(strerror(errno)));
    }

    tcflush(fd, TCIOFLUSH);
    return fd;
}
