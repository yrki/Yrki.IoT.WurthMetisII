using Microsoft.Extensions.Logging;

namespace Yrki.IoT.WurthMetisII.Features.MetisProtocol;

internal sealed class MetisProtocolService(ILogger<MetisProtocolService> logger) : IMetisProtocolService
{
    public byte[] BuildFrame(byte command, byte[] payload)
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

    public void SendCommandAndWait(
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
            logger.LogInformation("TX {Label} attempt {Attempt}/{Attempts}: {Hex}", label, attempt, attempts, Convert.ToHexString(command));
            serialStream.Write(command, 0, command.Length);
            serialStream.Flush();

            try
            {
                var response = WaitForFrame(serialStream, receiveBuffer, expectedCommand, timeoutMs);
                var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
                logger.LogInformation("[{Timestamp}] METIS CMD=0x{Command:X2} HEX={Hex}", timestamp, response.Command, Convert.ToHexString(response.RawFrame));
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

    public MetisFrame SendCommandAndGet(
        FileStream serialStream,
        List<byte> receiveBuffer,
        string label,
        byte[] command,
        byte expectedCommand,
        int timeoutMs = 1500)
    {
        logger.LogInformation("TX {Label}: {Hex}", label, Convert.ToHexString(command));
        serialStream.Write(command, 0, command.Length);
        serialStream.Flush();
        return WaitForFrame(serialStream, receiveBuffer, expectedCommand, timeoutMs);
    }

    public void SendCommandAndPause(
        FileStream serialStream,
        List<byte> receiveBuffer,
        string label,
        byte[] command,
        int pauseMs)
    {
        logger.LogInformation("TX {Label}: {Hex}", label, Convert.ToHexString(command));
        serialStream.Write(command, 0, command.Length);
        serialStream.Flush();
        DrainDuringPause(serialStream, receiveBuffer, pauseMs);
    }

    public bool TryExtractMetisFrame(List<byte> receiveBuffer, out MetisFrame frame)
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

    private MetisFrame WaitForFrame(FileStream serialStream, List<byte> receiveBuffer, byte expectedCommand, int timeoutMs)
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
            logger.LogDebug("[{Timestamp}] PAYLOAD: {Hex}", timestamp, Convert.ToHexString(chunk));
            receiveBuffer.AddRange(chunk.ToArray());

            while (TryExtractMetisFrame(receiveBuffer, out var frame))
            {
                logger.LogInformation("[{Timestamp}] METIS CMD=0x{Command:X2} LEN={Length} HEX={Hex}", timestamp, frame.Command, frame.Payload.Length, Convert.ToHexString(frame.RawFrame));
                if (frame.Command == expectedCommand)
                {
                    return frame;
                }
            }
        }

        throw new TimeoutException($"Timed out waiting for 0x{expectedCommand:X2}");
    }

    private void DrainDuringPause(FileStream serialStream, List<byte> receiveBuffer, int durationMs)
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
            logger.LogDebug("[{Timestamp}] PAYLOAD: {Hex}", timestamp, Convert.ToHexString(chunk));
            receiveBuffer.AddRange(chunk.ToArray());

            while (TryExtractMetisFrame(receiveBuffer, out var frame))
            {
                logger.LogInformation("[{Timestamp}] METIS CMD=0x{Command:X2} LEN={Length} HEX={Hex}", timestamp, frame.Command, frame.Payload.Length, Convert.ToHexString(frame.RawFrame));
            }
        }
    }
}
