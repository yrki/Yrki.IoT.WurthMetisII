# Yrki.IoT.WurthMetisII

This project reads Wireless M-Bus data from a Wurth Metis-II over a serial port and publishes the telegrams to MQTT.

The application is a small .NET console app that does the following:

1. Opens and configures the serial port connected to a Wurth Metis-II.
2. Reads raw Metis frames from the device.
3. Parses Wireless M-Bus telegrams.
4. Logs payloads locally to a file.
5. Publishes the telegrams to MQTT as JSON.

## What is sent to MQTT

When a Wireless M-Bus telegram is received, the application publishes a JSON message to the `wmbus/raw` topic.

At the moment, the application connects to:

- MQTT broker: `localhost`
- Port: `1883`
- Topic: `wmbus/raw` (configurable via `--topic`)

The JSON message contains:

- `payloadHex`: the Wireless M-Bus payload as a hex string
- `gatewayId`: the machine's hostname (`Environment.MachineName`)
- `rssi`: RSSI in dBm if the Metis device provides RSSI
- `timestamp`: UTC timestamp in ISO 8601 format

Example:

```json
{
  "payloadHex": "4409070556150008167A4D0030056FD9B0923064340B838E88CFAFB6BB890C691133A958CD40268BA482FAA542B6499432C8FE81E45AD0282350EBE79D6A",
  "gatewayId": "raspberrypi",
  "rssi": -87,
  "timestamp": "2026-04-02T09:15:30.1234567+00:00"
}
```

## Requirements

- .NET 10 SDK
- Access to the serial port connected to the Metis-II
- An MQTT broker available on `localhost:1883`
- `stty` available in the environment

In practice, the project is currently set up for macOS/Linux-style environments because the serial port is configured with `stty`, and the default port points to a macOS device path:

```text
/dev/cu.usbserial-53002FA7
```

## Build

```bash
dotnet build
```

## Run the application

Default startup:

```bash
dotnet run --project Yrki.IoT.WurthMetisII.csproj --
```

This uses the default values:

- port: `/dev/cu.usbserial-53002FA7`
- baud: `9600`
- log file: `payloads.log`
- topic: `wmbus/raw`

## Command-line arguments

The application supports the following arguments:

- `--port <port-name>`
  Use a specific serial port.

- `--baud <baudrate>`
  Set the serial port baud rate.

- `--activate`
  Send setup commands to the Metis-II before starting normal listening.

- `--dump-params`
  Read parameters from the Metis-II and exit afterwards.

- `--log-file <file-path>`
  Set the payload log file path.

- `--topic <topic>`
  Set the MQTT topic to publish to.

- `--help`
  Show usage information.

- `-h`
  Short form of `--help`.

## Usage syntax

```bash
dotnet run --project Yrki.IoT.WurthMetisII.csproj -- [--port <port>] [--baud <baudrate>] [--activate] [--dump-params] [--log-file <file>] [--topic <topic>]
```

## Common examples

Run with default settings:

```bash
dotnet run --project Yrki.IoT.WurthMetisII.csproj --
```

Run against a specific serial port:

```bash
dotnet run --project Yrki.IoT.WurthMetisII.csproj -- --port /dev/cu.usbserial-53002FA7
```

Run with an explicit baud rate:

```bash
dotnet run --project Yrki.IoT.WurthMetisII.csproj -- --port /dev/cu.usbserial-53002FA7 --baud 9600
```

Activate the Metis-II first, then start listening:

```bash
dotnet run --project Yrki.IoT.WurthMetisII.csproj -- --port /dev/cu.usbserial-53002FA7 --activate
```

Read parameters and exit:

```bash
dotnet run --project Yrki.IoT.WurthMetisII.csproj -- --port /dev/cu.usbserial-53002FA7 --dump-params
```

Write payload logs to a different file:

```bash
dotnet run --project Yrki.IoT.WurthMetisII.csproj -- --log-file logs/payloads.log
```

Show help:

```bash
dotnet run --project Yrki.IoT.WurthMetisII.csproj -- --help
```

## Startup flow

During normal startup, the application does the following:

1. Reads command-line arguments.
2. Creates the payload log file.
3. Configures the serial port.
4. Activates the Metis-II if `--activate` is specified.
5. Tries to read the RSSI configuration from the device.
6. Connects to MQTT on `localhost:1883`.
7. Starts continuously listening for Wireless M-Bus telegrams.
8. Logs and publishes incoming telegrams.

If MQTT is not available at startup, the application continues reading from the serial port and retries the MQTT connection later.

## Logging

The application uses `ILogger` with console logging.

In addition, `payloadHex` is written to a local log file, which by default is:

```text
payloads.log
```

Note that this file is currently written when the application attempts to forward a payload, not only when MQTT publishing has definitely succeeded.

## Architecture

The project is split by functionality under `Features/`:

- `Features/Application`
  Overall application orchestration.

- `Features/Arguments`
  Command-line argument parsing.

- `Features/Serial`
  Serial port opening and configuration.

- `Features/MetisProtocol`
  Building and parsing Metis frames.

- `Features/Gateway`
  Gateway identity for this device.

- `Features/Activation`
  Metis-II setup and activation.

- `Features/Parameters`
  Reading parameters from the device.

- `Features/Telegrams`
  Wireless M-Bus telegram parsing and handling.

- `Features/ServerTransport`
  Transport abstraction for forwarding data.

The transport layer uses a generic interface:

- `ISendToServer`

At the moment, the available implementation is:

- `SendToServerWithMqttService`

This makes it straightforward to add more implementations later, for example:

- `SendToServerWithRest`
- `SendToServerWithEventHub`

## Stopping the application

Press `Ctrl+C` to stop the application.

## Troubleshooting

If you are not receiving data:

- Check that the correct serial port is being used.
- Check that the device responds on the selected baud rate.
- Check that the process has permission to access the serial port.
- Check that the MQTT broker is running on `localhost:1883`.
- Try `--activate` if the device has not been configured yet.
- Try `--dump-params` to verify communication with the Metis-II.
