using System.Diagnostics;
using MQTTnet;

var options = ParseArgs(args);

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    Environment.Exit(0);
};

Console.WriteLine($"Opening {options.PortName} at {options.BaudRate} baud");
ConfigurePort(options.PortName, options.BaudRate);

if (options.Activate)
{
    ActivateMetisPlug(options.PortName, options.BaudRate);
}

if (args.Contains("--dump-params"))
{
    DumpAllParameters(options.PortName, options.BaudRate);
    return;
}

ConfigurePort(options.PortName, options.BaudRate);
using var serialStream = OpenStream(options.PortName);

// Check if RSSI is enabled (CMD_GET_REQ param=0x45, len=0x01)
var rssiEnabled = false;
var checkBuffer = new List<byte>(256);
try
{
    var response = SendCommandAndGet(serialStream, checkBuffer, "CMD_GET_REQ RSSI_ENABLE",
        BuildFrame(0x0A, [0x45, 0x01]), 0x8A);
    // Response payload: param(0x45) + len(0x01) + value
    rssiEnabled = response.Payload.Length >= 3 && response.Payload[2] == 0x01;
    Console.WriteLine($"RSSI_ENABLE = {rssiEnabled}");
}
catch (TimeoutException)
{
    Console.WriteLine("Could not read RSSI_ENABLE, assuming disabled");
}

// Connect MQTT
var mqttClient = new MqttClientFactory().CreateMqttClient();
var mqttOptions = new MqttClientOptionsBuilder()
    .WithTcpServer("localhost", 1883)
    .Build();

try
{
    await mqttClient.ConnectAsync(mqttOptions);
    Console.WriteLine("MQTT connected to localhost:1883");
}
catch (Exception ex)
{
    Console.WriteLine($"MQTT connection failed: {ex.Message}");
    Console.WriteLine("Continuing without MQTT");
}

Console.WriteLine("Listening for WMBus telegrams. Press Ctrl+C to stop.");

var chunkBuffer = new byte[4096];
var metisBuffer = new List<byte>(8192);

while (true)
{
    try
    {
        var bytesRead = serialStream.Read(chunkBuffer, 0, chunkBuffer.Length);
        if (bytesRead <= 0)
        {
            Thread.Sleep(25);
            continue;
        }

        var chunk = chunkBuffer.AsSpan(0, bytesRead);
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        Console.WriteLine($"[{timestamp}] PAYLOAD: {ToHex(chunk)}");
        metisBuffer.AddRange(chunk.ToArray());
        await DumpParsedFramesAsync(metisBuffer, rssiEnabled, mqttClient);
    }
    catch (IOException)
    {
        Thread.Sleep(25);
    }
}

static Options ParseArgs(string[] args)
{
    var portName = "/dev/cu.usbserial-53002FA7";
    var baudRate = 9600;
    var activate = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--port" when i + 1 < args.Length:
                portName = args[++i];
                break;
            case "--baud" when i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedBaudRate):
                baudRate = parsedBaudRate;
                i++;
                break;
            case "--activate":
                activate = true;
                break;
            case "--help":
            case "-h":
                Console.WriteLine("Usage: dotnet run -- [--port /dev/cu.usbserial-53002FA7] [--baud 9600] [--activate]");
                Environment.Exit(0);
                break;
        }
    }

    return new Options(portName, baudRate, activate);
}

static void DumpAllParameters(string portName, int baudRate)
{
    Console.WriteLine("Reading parameters 0x00..0x50");
    ConfigurePort(portName, baudRate);
    using var stream = OpenStream(portName);
    var buf = new List<byte>(256);

    for (byte param = 0x00; param <= 0x50; param++)
    {
        try
        {
            var response = SendCommandAndGet(stream, buf, $"GET 0x{param:X2}",
                BuildFrame(0x0A, [param, 0x01]), 0x8A, timeoutMs: 500);

            // Payload: param + len + value(s)
            if (response.Payload.Length >= 2)
            {
                var pLen = response.Payload[1];
                var values = response.Payload.AsSpan(2);
                var dec = string.Join(", ", values.ToArray().Select(v => v.ToString()));
                Console.WriteLine($"  [0x{param:X2}] len={pLen} hex={ToHex(values)} dec={dec}");
            }
            else
            {
                Console.WriteLine($"  [0x{param:X2}] raw={ToHex(response.Payload)}");
            }
        }
        catch (TimeoutException)
        {
            Console.WriteLine($"  [0x{param:X2}] (no response)");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"  [0x{param:X2}] ERROR: {ex.Message}");
        }
    }
}

static void ActivateMetisPlug(string portName, int baudRate)
{
    Console.WriteLine("Activating Metis-II Plug with minimal collector setup");

    ConfigurePort(portName, baudRate);
    using var serialStream = OpenStream(portName);
    var receiveBuffer = new List<byte>(1024);

    // Metis-II Plug collector setup from the Plug/module manuals:
    // enable UART command output, enable RSSI, store Mode_Preselect=C1_meter,
    // reset to apply, then switch runtime mode to C2_T2_other.
    SendCommandAndWait(serialStream, receiveBuffer, "CMD_SET_REQ UART_CMD_OUT_ENABLE=1", BuildFrame(0x09, [0x05, 0x01, 0x01]), 0x89);
    SendCommandAndWait(serialStream, receiveBuffer, "CMD_SET_REQ RSSI_ENABLE=1", BuildFrame(0x09, [0x45, 0x01, 0x01]), 0x89);
    SendCommandAndWait(serialStream, receiveBuffer, "CMD_SET_REQ MODE_PRESELECT=C2_T2_other", BuildFrame(0x09, [0x46, 0x01, 0x09]), 0x89);
    SendCommandAndPause(serialStream, receiveBuffer, "CMD_RESET_REQ", BuildFrame(0x05, []), 1200);

    ConfigurePort(portName, baudRate);
    using var runtimeStream = OpenStream(portName);
    var runtimeBuffer = new List<byte>(1024);
    SendCommandAndWait(runtimeStream, runtimeBuffer, "CMD_SET_MODE_REQ C2_T2_other", BuildFrame(0x04, [0x09]), 0x84);
}

static FileStream OpenStream(string portName)
{
    return new FileStream(
        portName,
        FileMode.Open,
        FileAccess.ReadWrite,
        FileShare.ReadWrite,
        bufferSize: 4096);
}

static void ConfigurePort(string portName, int baudRate)
{
    var arguments = $"-f {portName} {baudRate} cs8 -cstopb -parenb raw -ixon -ixoff min 0 time 1";
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "stty",
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        }
    };

    process.Start();
    process.WaitForExit();

    if (process.ExitCode == 0)
    {
        return;
    }

    var error = process.StandardError.ReadToEnd().Trim();
    throw new IOException($"Failed to configure serial port {portName}: {error}");
}

static byte[] BuildFrame(byte command, byte[] payload)
{
    var frame = new byte[payload.Length + 4];
    frame[0] = 0xFF;
    frame[1] = command;
    frame[2] = (byte)payload.Length;
    Array.Copy(payload, 0, frame, 3, payload.Length);

    byte checksum = 0x00;
    for (var i = 0; i < frame.Length - 1; i++)
    {
        checksum ^= frame[i];
    }

    frame[^1] = checksum;
    return frame;
}

static void SendCommandAndWait(
    FileStream serialStream,
    List<byte> receiveBuffer,
    string label,
    byte[] command,
    byte expectedCommand,
    int timeoutMs = 1500,
    int attempts = 5)
{
    Exception? lastError = null;

    for (var attempt = 1; attempt <= attempts; attempt++)
    {
        Console.WriteLine($"TX {label} attempt {attempt}/{attempts}: {ToHex(command)}");
        serialStream.Write(command, 0, command.Length);
        serialStream.Flush();

        try
        {
            var response = WaitForFrame(serialStream, receiveBuffer, expectedCommand, timeoutMs);
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
            Console.WriteLine($"[{timestamp}] METIS CMD=0x{response.Command:X2} HEX={ToHex(response.RawFrame)}");
            if (response.Payload.Length > 0 && response.Payload[0] != 0x00)
            {
                throw new IOException($"{label} failed with status 0x{response.Payload[0]:X2}");
            }

            Thread.Sleep(80);
            return;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            lastError = ex;
            DrainDuringPause(serialStream, receiveBuffer, 250);
            Thread.Sleep(120);
        }
    }

    throw lastError ?? new TimeoutException($"Timed out waiting for 0x{expectedCommand:X2}");
}

static MetisFrame SendCommandAndGet(
    FileStream serialStream,
    List<byte> receiveBuffer,
    string label,
    byte[] command,
    byte expectedCommand,
    int timeoutMs = 1500)
{
    Console.WriteLine($"TX {label}: {ToHex(command)}");
    serialStream.Write(command, 0, command.Length);
    serialStream.Flush();
    return WaitForFrame(serialStream, receiveBuffer, expectedCommand, timeoutMs);
}

static void SendCommandAndPause(
    FileStream serialStream,
    List<byte> receiveBuffer,
    string label,
    byte[] command,
    int pauseMs)
{
    Console.WriteLine($"TX {label}: {ToHex(command)}");
    serialStream.Write(command, 0, command.Length);
    serialStream.Flush();
    DrainDuringPause(serialStream, receiveBuffer, pauseMs);
}

static MetisFrame WaitForFrame(FileStream serialStream, List<byte> receiveBuffer, byte expectedCommand, int timeoutMs)
{
    var startedAt = Environment.TickCount64;
    var buffer = new byte[512];

    while (Environment.TickCount64 - startedAt < timeoutMs)
    {
        var bytesRead = serialStream.Read(buffer, 0, buffer.Length);
        if (bytesRead <= 0)
        {
            Thread.Sleep(25);
            continue;
        }

        var chunk = buffer.AsSpan(0, bytesRead);
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        Console.WriteLine($"[{timestamp}] PAYLOAD: {ToHex(chunk)}");
        receiveBuffer.AddRange(chunk.ToArray());

        while (TryExtractMetisFrame(receiveBuffer, out var frame))
        {
            Console.WriteLine($"[{timestamp}] METIS CMD=0x{frame.Command:X2} LEN={frame.Payload.Length} HEX={ToHex(frame.RawFrame)}");
            if (frame.Command == expectedCommand)
            {
                return frame;
            }
        }
    }

    throw new TimeoutException($"Timed out waiting for 0x{expectedCommand:X2}");
}

static void DrainDuringPause(FileStream serialStream, List<byte> receiveBuffer, int durationMs)
{
    var startedAt = Environment.TickCount64;
    var buffer = new byte[512];

    while (Environment.TickCount64 - startedAt < durationMs)
    {
        var bytesRead = serialStream.Read(buffer, 0, buffer.Length);
        if (bytesRead <= 0)
        {
            Thread.Sleep(25);
            continue;
        }

        var chunk = buffer.AsSpan(0, bytesRead);
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        Console.WriteLine($"[{timestamp}] PAYLOAD: {ToHex(chunk)}");
        receiveBuffer.AddRange(chunk.ToArray());

        while (TryExtractMetisFrame(receiveBuffer, out var frame))
        {
            Console.WriteLine($"[{timestamp}] METIS CMD=0x{frame.Command:X2} LEN={frame.Payload.Length} HEX={ToHex(frame.RawFrame)}");
        }
    }
}

static async Task DumpParsedFramesAsync(List<byte> receiveBuffer, bool rssiEnabled, IMqttClient mqttClient)
{
    while (TryExtractMetisFrame(receiveBuffer, out var frame))
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");

        if (frame.Command == 0x03)
        {
            await PrintWMBusTelegramAsync(timestamp, frame, rssiEnabled, mqttClient);
            continue;
        }

        Console.WriteLine($"[{timestamp}] METIS CMD=0x{frame.Command:X2} LEN={frame.Payload.Length} HEX={ToHex(frame.RawFrame)}");
    }
}

static async Task PrintWMBusTelegramAsync(string timestamp, MetisFrame frame, bool rssiEnabled, IMqttClient mqttClient)
{
    var payload = frame.Payload;

    if (payload.Length < 11)
    {
        Console.WriteLine($"[{timestamp}] CMD_DATA_IND ({payload.Length}B): {ToHex(payload)}");
        return;
    }

    int? rssiDbm = null;
    ReadOnlySpan<byte> wmbus;

    if (rssiEnabled && payload.Length >= 12)
    {
        var rssi = payload[^1];
        rssiDbm = rssi >= 128 ? (rssi - 256) / 2 - 74 : rssi / 2 - 74;
        wmbus = payload.AsSpan(0, payload.Length - 1);
    }
    else
    {
        wmbus = payload.AsSpan();
    }

    // WMBus link-layer: L C M M ID ID ID ID Ver DevType [CI ...]
    // wmbus[0] = L-field (length of remaining bytes after L-field)
    var lField = wmbus[0];
    var cField = wmbus[1];
    var mfr = (ushort)(wmbus[2] | (wmbus[3] << 8));
    var mfrStr = DecodeMfr(mfr);
    var id = DecodeBcd(wmbus.Slice(4, 4));
    var version = wmbus[8];
    var deviceType = wmbus[9];

    var rssiStr = rssiDbm.HasValue ? $" RSSI={rssiDbm}dBm" : "";
    Console.WriteLine($"[{timestamp}] WMBus L={lField} C=0x{cField:X2} Mfr={mfrStr ?? "???"} Id={id ?? "????????"} Ver=0x{version:X2} Dev=0x{deviceType:X2}{rssiStr}");

    if (wmbus.Length > 10)
    {
        var ciField = wmbus[10];
        Console.WriteLine($"  CI=0x{ciField:X2} AppData({wmbus.Length - 11}B): {ToHex(wmbus[11..].ToArray())}");
    }

    Console.WriteLine($"  RAW({wmbus.Length}B): {ToHex(wmbus.ToArray())}");
    Console.WriteLine();

    // Publish to MQTT
    if (mqttClient.IsConnected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            payloadHex = ToHex(wmbus.ToArray()),
            gatewayId = "ABC123",
            rssi = rssiDbm,
            timestamp = DateTimeOffset.UtcNow.ToString("o")
        });

        var message = new MqttApplicationMessageBuilder()
            .WithTopic("wmbus/raw")
            .WithPayload(json)
            .Build();

        try
        {
            await mqttClient.PublishAsync(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  MQTT publish failed: {ex.Message}");
        }
    }
}

static string? DecodeMfr(ushort code)
{
    var a = (char)(((code >> 10) & 0x1F) + 64);
    var b = (char)(((code >> 5) & 0x1F) + 64);
    var c = (char)((code & 0x1F) + 64);
    return a is >= 'A' and <= 'Z' && b is >= 'A' and <= 'Z' && c is >= 'A' and <= 'Z'
        ? $"{a}{b}{c}" : null;
}

static string? DecodeBcd(ReadOnlySpan<byte> bcd)
{
    Span<char> chars = stackalloc char[bcd.Length * 2];
    var idx = 0;
    for (var i = bcd.Length - 1; i >= 0; i--)
    {
        var hi = (bcd[i] >> 4) & 0x0F;
        var lo = bcd[i] & 0x0F;
        if (hi > 9 || lo > 9) return null;
        chars[idx++] = (char)('0' + hi);
        chars[idx++] = (char)('0' + lo);
    }
    return new string(chars);
}

static bool TryExtractMetisFrame(List<byte> receiveBuffer, out MetisFrame frame)
{
    frame = default;

    while (receiveBuffer.Count > 0)
    {
        if (receiveBuffer[0] != 0xFF)
        {
            receiveBuffer.RemoveAt(0);
            continue;
        }

        if (receiveBuffer.Count < 4)
        {
            return false;
        }

        var payloadLength = receiveBuffer[2];
        var totalLength = payloadLength + 4;
        if (receiveBuffer.Count < totalLength)
        {
            return false;
        }

        var rawFrame = receiveBuffer.GetRange(0, totalLength).ToArray();
        receiveBuffer.RemoveRange(0, totalLength);

        byte checksum = 0x00;
        for (var i = 0; i < rawFrame.Length - 1; i++)
        {
            checksum ^= rawFrame[i];
        }

        if (checksum != rawFrame[^1])
        {
            continue;
        }

        frame = new MetisFrame(rawFrame[1], rawFrame.AsSpan(3, payloadLength).ToArray(), rawFrame);
        return true;
    }

    return false;
}

static string ToHex(ReadOnlySpan<byte> data)
{
    return Convert.ToHexString(data.ToArray());
}

internal sealed record Options(string PortName, int BaudRate, bool Activate);
internal readonly record struct MetisFrame(byte Command, byte[] Payload, byte[] RawFrame);
