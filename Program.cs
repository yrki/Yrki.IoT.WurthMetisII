using System.Diagnostics;
using System.Text;

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
    ActivateMetis(options.PortName, options.ActivationMode);
}

var listenBaudRate = options.BaudRate;
ConfigurePort(options.PortName, listenBaudRate);
using var serialStream = new FileStream(
    options.PortName,
    FileMode.Open,
    FileAccess.ReadWrite,
    FileShare.ReadWrite,
    bufferSize: 4096);

Console.WriteLine("Listening for raw bytes. Press Ctrl+C to stop.");

var buffer = new byte[4096];
var receiveBuffer = new List<byte>(8192);

while (true)
{
    try
    {
        var bytesRead = serialStream.Read(buffer, 0, buffer.Length);
        if (bytesRead <= 0)
        {
            Thread.Sleep(25);
            continue;
        }

        var payload = buffer.AsSpan(0, bytesRead);
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        Console.WriteLine($"[{timestamp}] PAYLOAD: {ToHex(payload)}");
        receiveBuffer.AddRange(payload.ToArray());
        DumpIncomingMessages(receiveBuffer);
    }
    catch (IOException)
    {
        Thread.Sleep(25);
    }
}

static Options ParseArgs(string[] args)
{
    const string defaultPortName = "/dev/tty.usbserial-53002FA7";
    const int defaultBaudRate = 115200;

    var portName = defaultPortName;
    var baudRate = defaultBaudRate;
    var activate = false;
    var activationMode = ActivationMode.T2Other;

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
            case "--mode" when i + 1 < args.Length:
                activationMode = ParseActivationMode(args[++i]);
                break;
            case "--help":
            case "-h":
                Console.WriteLine("Usage: dotnet run -- [--port /dev/tty.usbserial-53002FA7] [--baud 9600] [--activate] [--mode t2]");
                Environment.Exit(0);
                break;
        }
    }

    return new Options(portName, baudRate, activate, activationMode);
}

static string ToHex(ReadOnlySpan<byte> buffer)
{
    return Convert.ToHexString(buffer.ToArray());
}

static void ConfigurePort(string portName, int baudRate)
{
    var arguments = $"-f {portName} {baudRate} cs8 -cstopb -parenb -crtscts raw -ixon -ixoff min 0 time 1";
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
    if (string.IsNullOrWhiteSpace(error))
    {
        error = "Unknown stty error";
    }

    throw new IOException($"Failed to configure serial port {portName}: {error}");
}

static void ActivateMetis(string portName, ActivationMode mode)
{
    Console.WriteLine("Activating Metis-II using minimal receiver setup");

    ConfigurePort(portName, 115200);
    using var serialStream = new FileStream(
        portName,
        FileMode.Open,
        FileAccess.ReadWrite,
        FileShare.ReadWrite,
        bufferSize: 4096);
    var receiveBuffer = new List<byte>(1024);

    SendCommandAndWait(serialStream, receiveBuffer, "CMD_SET_REQ UART_CMD_OUT_ENABLE=1", "FF0903050101F0", expectedCommand: 0x89);
    SendCommandAndWait(serialStream, receiveBuffer, "CMD_SET_REQ RSSI_Enable=1", "FF0903450101B0", expectedCommand: 0x89);
    SendCommandAndWait(
        serialStream,
        receiveBuffer,
        $"CMD_SET_REQ Mode_Preselect={mode}",
        GetSetModePreselectCommandHex(mode),
        expectedCommand: 0x89);
    SendCommandAndPause(serialStream, receiveBuffer, "CMD_RESET_REQ", "FF0500FA", pauseMs: 1000);

    serialStream.Flush();
    Console.WriteLine("Keeping host UART at 115200 baud after activation");

    ConfigurePort(portName, 115200);
    using var runtimeStream = new FileStream(
        portName,
        FileMode.Open,
        FileAccess.ReadWrite,
        FileShare.ReadWrite,
        bufferSize: 4096);
    var runtimeReceiveBuffer = new List<byte>(1024);

    SendCommandAndWait(
        runtimeStream,
        runtimeReceiveBuffer,
        $"CMD_SET_MODE_REQ {mode}",
        GetSetModeCommandHex(mode),
        expectedCommand: 0x84);
    runtimeStream.Flush();
}

static void SendCommandAndWait(
    FileStream serialStream,
    List<byte> receiveBuffer,
    string label,
    string hexPayload,
    byte expectedCommand,
    int timeoutMs = 3000,
    int maxAttempts = 5)
{
    var bytes = Convert.FromHexString(hexPayload);
    Exception? lastError = null;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        Console.WriteLine($"TX {label} attempt {attempt}/{maxAttempts}: {hexPayload}");
        serialStream.Write(bytes, 0, bytes.Length);
        serialStream.Flush();

        try
        {
            var response = WaitForMetisFrame(serialStream, receiveBuffer, expectedCommand, timeoutMs);
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
            Console.WriteLine($"[{timestamp}] RX CMD=0x{response.Command:X2} HEX={ToHex(response.RawFrame)}");

            if (response.Payload.Length > 0 && response.Payload[0] != 0x00)
            {
                throw new IOException($"{label} failed with status 0x{response.Payload[0]:X2}");
            }

            Thread.Sleep(80);
            return;
        }
        catch (TimeoutException ex)
        {
            lastError = ex;
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
            Console.WriteLine($"[{timestamp}] Timeout waiting for CNF 0x{expectedCommand:X2} after {label}");
            DrainIncoming(serialStream, receiveBuffer, 250);
            Thread.Sleep(120);
        }
    }

    throw lastError ?? new TimeoutException($"Timed out waiting for Metis response 0x{expectedCommand:X2}");
}

static void SendCommandAndPause(
    FileStream serialStream,
    List<byte> receiveBuffer,
    string label,
    string hexPayload,
    int pauseMs)
{
    var bytes = Convert.FromHexString(hexPayload);
    Console.WriteLine($"TX {label}: {hexPayload}");
    serialStream.Write(bytes, 0, bytes.Length);
    serialStream.Flush();

    DrainIncoming(serialStream, receiveBuffer, pauseMs);
    Thread.Sleep(80);
}

static MetisFrame WaitForMetisFrame(
    FileStream serialStream,
    List<byte> receiveBuffer,
    byte expectedCommand,
    int timeoutMs)
{
    var buffer = new byte[512];
    var startedAt = Environment.TickCount64;

    while (Environment.TickCount64 - startedAt < timeoutMs)
    {
        var bytesRead = serialStream.Read(buffer, 0, buffer.Length);
        if (bytesRead > 0)
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
            var rawBytes = buffer.AsSpan(0, bytesRead);
            Console.WriteLine($"[{timestamp}] PAYLOAD: {ToHex(rawBytes)}");
            receiveBuffer.AddRange(rawBytes.ToArray());

            while (TryExtractMetisFrame(receiveBuffer, out var frame))
            {
                Console.WriteLine($"[{timestamp}] METIS CMD=0x{frame.Command:X2} LEN={frame.Length} HEX={ToHex(frame.RawFrame)}");
                if (frame.Command == expectedCommand)
                {
                    return frame;
                }
            }
        }
        else
        {
            Thread.Sleep(25);
        }
    }

    throw new TimeoutException($"Timed out waiting for Metis response 0x{expectedCommand:X2}");
}

static void DrainIncoming(FileStream serialStream, List<byte> receiveBuffer, int durationMs)
{
    var buffer = new byte[512];
    var startedAt = Environment.TickCount64;

    while (Environment.TickCount64 - startedAt < durationMs)
    {
        var bytesRead = serialStream.Read(buffer, 0, buffer.Length);
        if (bytesRead > 0)
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
            var payload = buffer.AsSpan(0, bytesRead);
            Console.WriteLine($"[{timestamp}] PAYLOAD: {ToHex(payload)}");
            receiveBuffer.AddRange(payload.ToArray());

            while (TryExtractMetisFrame(receiveBuffer, out var frame))
            {
                Console.WriteLine($"[{timestamp}] METIS CMD=0x{frame.Command:X2} LEN={frame.Length} HEX={ToHex(frame.RawFrame)}");
            }
        }
        else
        {
            Thread.Sleep(25);
        }
    }
}

static ActivationMode ParseActivationMode(string value)
{
    return value.ToLowerInvariant() switch
    {
        "t2" => ActivationMode.T2Other,
        "t2other" => ActivationMode.T2Other,
        "c2t2" => ActivationMode.C2T2Other,
        "t1" => ActivationMode.C2T2Other,
        "t1meter" => ActivationMode.C2T2Other,
        "c1" => ActivationMode.C2T2Other,
        "s2" => ActivationMode.S2,
        _ => throw new ArgumentException($"Unsupported mode '{value}'. Use 't2', 'c2t2' or 's2'.")
    };
}

static string GetSetModeCommandHex(ActivationMode mode)
{
    return mode switch
    {
        ActivationMode.T2Other => "FF040108F2",
        ActivationMode.C2T2Other => "FF040109F3",
        ActivationMode.S2 => "FF040103F9",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}

static string GetSetModePreselectCommandHex(ActivationMode mode)
{
    return mode switch
    {
        ActivationMode.T2Other => "FF0903460108BA",
        ActivationMode.C2T2Other => "FF0903460109BB",
        ActivationMode.S2 => "FF0903460103B1",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}

static void DumpIncomingMessages(List<byte> receiveBuffer)
{
    while (TryExtractMetisFrame(receiveBuffer, out var metisFrame))
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        switch (metisFrame.Command)
        {
            case 0x03:
                DumpDataIndication(timestamp, metisFrame);
                break;
            case 0x80:
            case 0x84:
            case 0x89:
            case 0x90:
            case 0x91:
                break;
            default:
                Console.WriteLine($"[{timestamp}] METIS CMD=0x{metisFrame.Command:X2} LEN={metisFrame.Payload.Length} HEX={ToHex(metisFrame.RawFrame)}");
                break;
        }
    }
}

static void DumpDataIndication(string timestamp, MetisFrame metisFrame)
{
    if (!TryExtractWmbusFrame(metisFrame, out var frame, out var rssi))
    {
        Console.WriteLine($"[{timestamp}] CMD_DATA_IND malformed LEN={metisFrame.Length} HEX={ToHex(metisFrame.RawFrame)}");
        Console.WriteLine($"PAYLOAD: {ToHex(metisFrame.Payload)}");
        Console.WriteLine();
        return;
    }

    Console.WriteLine($"[{timestamp}] WMBus frame {frame.Length} bytes{(rssi is null ? string.Empty : $" RSSI=0x{rssi.Value:X2}")}");
    Console.WriteLine($"PAYLOAD: {ToHex(frame)}");

    if (TryParseLinkLayerHeader(frame, out var header))
    {
        Console.WriteLine(
            $"HEADER Mfr={header.Manufacturer} ({header.ManufacturerCode:X4}) UniqueId={header.UniqueId} Version=0x{header.Version:X2} DeviceType=0x{header.DeviceType:X2} C=0x{header.Control:X2}");
    }
    else
    {
        Console.WriteLine("HEADER Unable to decode standard WMBus link-layer header");
    }

    Console.WriteLine();
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

        if (!HasValidChecksum(rawFrame))
        {
            continue;
        }

        frame = new MetisFrame(
            rawFrame[1],
            payloadLength,
            rawFrame.AsSpan(3, payloadLength).ToArray(),
            rawFrame);
        return true;
    }

    return false;
}

static bool HasValidChecksum(ReadOnlySpan<byte> frame)
{
    if (frame.Length < 4)
    {
        return false;
    }

    byte checksum = 0x00;
    for (var i = 0; i < frame.Length - 1; i++)
    {
        checksum ^= frame[i];
    }

    return checksum == frame[^1];
}

static bool TryExtractWmbusFrame(MetisFrame metisFrame, out byte[] frame, out byte? rssi)
{
    frame = Array.Empty<byte>();
    rssi = null;

    if (metisFrame.Length < 9 || metisFrame.Payload.Length != metisFrame.Length)
    {
        return false;
    }

    if (TryBuildWmbusFrame(metisFrame, hasRssi: true, out frame, out rssi) && TryParseLinkLayerHeader(frame, out _))
    {
        return true;
    }

    if (TryBuildWmbusFrame(metisFrame, hasRssi: false, out frame, out rssi))
    {
        return true;
    }

    return false;
}

static bool TryBuildWmbusFrame(MetisFrame metisFrame, bool hasRssi, out byte[] frame, out byte? rssi)
{
    frame = Array.Empty<byte>();
    rssi = null;

    if (hasRssi)
    {
        if (metisFrame.Length < 10)
        {
            return false;
        }

        var wmbusLength = metisFrame.Length - 1;
        frame = PrefixByte((byte)wmbusLength, metisFrame.Payload.AsSpan(0, wmbusLength));
        rssi = metisFrame.Payload[^1];
        return true;
    }

    frame = PrefixByte((byte)metisFrame.Length, metisFrame.Payload);
    return true;
}

static byte[] PrefixByte(byte prefix, ReadOnlySpan<byte> data)
{
    var result = new byte[data.Length + 1];
    result[0] = prefix;
    data.CopyTo(result.AsSpan(1));
    return result;
}

static bool TryParseLinkLayerHeader(ReadOnlySpan<byte> frame, out LinkLayerHeader header)
{
    header = default;

    if (frame.Length < 11)
    {
        return false;
    }

    var manufacturerCode = (ushort)(frame[3] | (frame[4] << 8));
    var manufacturer = DecodeManufacturer(manufacturerCode);
    var uniqueId = DecodeBcdId(frame.Slice(5, 4));

    if (manufacturer is null || uniqueId is null)
    {
        return false;
    }

    header = new LinkLayerHeader(
        frame[2],
        manufacturerCode,
        manufacturer,
        uniqueId,
        frame[9],
        frame[10]);

    return true;
}

static string? DecodeManufacturer(ushort manufacturerCode)
{
    var first = (char)(((manufacturerCode >> 10) & 0x1F) + 64);
    var second = (char)(((manufacturerCode >> 5) & 0x1F) + 64);
    var third = (char)((manufacturerCode & 0x1F) + 64);

    return IsUppercaseLetter(first) && IsUppercaseLetter(second) && IsUppercaseLetter(third)
        ? new string([first, second, third])
        : null;
}

static bool IsUppercaseLetter(char value)
{
    return value is >= 'A' and <= 'Z';
}

static string? DecodeBcdId(ReadOnlySpan<byte> packedBcd)
{
    Span<char> chars = stackalloc char[packedBcd.Length * 2];
    var index = 0;

    for (var i = packedBcd.Length - 1; i >= 0; i--)
    {
        var high = (packedBcd[i] >> 4) & 0x0F;
        var low = packedBcd[i] & 0x0F;
        if (high > 9 || low > 9)
        {
            return null;
        }

        chars[index++] = (char)('0' + high);
        chars[index++] = (char)('0' + low);
    }

    return new string(chars);
}

internal sealed record Options(string PortName, int BaudRate, bool Activate, ActivationMode ActivationMode);
internal enum ActivationMode
{
    S2,
    T2Other,
    C2T2Other
}
internal readonly record struct MetisFrame(byte Command, int Length, byte[] Payload, byte[] RawFrame);
internal readonly record struct LinkLayerHeader(
    byte Control,
    ushort ManufacturerCode,
    string Manufacturer,
    string UniqueId,
    byte Version,
    byte DeviceType);
