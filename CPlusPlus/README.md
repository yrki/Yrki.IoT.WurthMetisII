# C++ Native Build

This folder contains the C++ version of the WMBus gateway that is being migrated from the C# implementation.

## macOS Apple Silicon

The project builds and runs natively on macOS Apple Silicon, including M-series machines such as M5.

### Prerequisites

- Xcode Command Line Tools
- CMake
- Homebrew
- Mosquitto client library

Install the required packages:

```bash
xcode-select --install
brew install cmake mosquitto
```

### Build

From the `CPlusPlus` directory:

```bash
./build-native.sh
```

This builds the binary here:

```text
build-native/wmbus-gateway
```

### Run

Show usage:

```bash
./run-native.sh --help
```

Run with the macOS default serial port used in this repository:

```bash
./run-native.sh --port /dev/cu.usbserial-53002FA7 --baud 9600
```

Activate the Metis-II first, then start listening:

```bash
./run-native.sh --port /dev/cu.usbserial-53002FA7 --baud 9600 --activate
```

Read Metis-II parameters and exit:

```bash
./run-native.sh --port /dev/cu.usbserial-53002FA7 --dump-params
```

Use a custom MQTT broker:

```bash
./run-native.sh --port /dev/cu.usbserial-53002FA7 --mqtt-host localhost --mqtt-port 1883 --topic wmbus/raw
```

### Default behavior

On macOS, the default serial port is:

```text
/dev/cu.usbserial-53002FA7
```

Other defaults:

- baud rate: `9600`
- MQTT host: `localhost`
- MQTT port: `1883`
- MQTT topic: `wmbus/raw`

### Notes

- The binary links against Homebrew's `libmosquitto`.
- If your device appears under a different serial path, pass it with `--port`.
- You can list candidate serial devices on macOS with:

```bash
ls /dev/cu.*
```
