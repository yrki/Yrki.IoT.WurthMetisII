using Microsoft.Extensions.Logging;
using Yrki.IoT.WurthMetisII.Features.Arguments;
using Yrki.IoT.WurthMetisII.Features.MetisProtocol;
using Yrki.IoT.WurthMetisII.Features.Serial;
using Yrki.IoT.WurthMetisII.Features.ServerTransport;

namespace Yrki.IoT.WurthMetisII.Features.Telegrams;

internal sealed class TelegramListenerService(
    ILogger<TelegramListenerService> logger,
    ISerialPortService serialPortService,
    IMetisProtocolService metisProtocolService,
    IWMBusTelegramParserService telegramParserService,
    ISendToServer sendToServer) : ITelegramListenerService
{
    public async Task ListenAsync(RuntimeOptions options, CancellationToken cancellationToken)
    {
        serialPortService.ConfigurePort(options.PortName, options.BaudRate);
        using var serialStream = serialPortService.OpenStream(options.PortName);

        var rssiEnabled = TryReadRssiEnabled(serialStream);
        var chunkBuffer = new byte[4096];
        var metisBuffer = new List<byte>(8192);

        logger.LogInformation("Listening for WMBus telegrams. Press Ctrl+C to stop.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bytesRead = serialStream.Read(chunkBuffer, 0, chunkBuffer.Length);
                if (bytesRead <= 0)
                {
                    await Task.Delay(25, cancellationToken);
                    continue;
                }

                var chunk = chunkBuffer.AsSpan(0, bytesRead);
                var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
                logger.LogDebug("[{Timestamp}] PAYLOAD: {Hex}", timestamp, Convert.ToHexString(chunk));
                metisBuffer.AddRange(chunk.ToArray());
                await DumpParsedFramesAsync(metisBuffer, rssiEnabled, options.MqttTopic, cancellationToken);
            }
            catch (IOException)
            {
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    private bool TryReadRssiEnabled(FileStream serialStream)
    {
        var receiveBuffer = new List<byte>(256);

        try
        {
            var response = metisProtocolService.SendCommandAndGet(
                serialStream,
                receiveBuffer,
                "CMD_GET_REQ RSSI_ENABLE",
                metisProtocolService.BuildFrame(0x0A, [0x45, 0x01]),
                0x8A);

            var rssiEnabled = response.Payload.Length >= 3 && response.Payload[2] == 0x01;
            logger.LogInformation("RSSI_ENABLE = {RssiEnabled}", rssiEnabled);
            return rssiEnabled;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Could not read RSSI_ENABLE, assuming disabled");
            return false;
        }
    }

    private async Task DumpParsedFramesAsync(List<byte> receiveBuffer, bool rssiEnabled, string topic, CancellationToken cancellationToken)
    {
        while (metisProtocolService.TryExtractMetisFrame(receiveBuffer, out var frame))
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");

            if (frame.Command == 0x03)
            {
                var payload = telegramParserService.ParseAndPrint(timestamp, frame, rssiEnabled, topic);
                if (payload is not null)
                {
                    await sendToServer.SendAsync(payload, cancellationToken);
                }

                continue;
            }

            logger.LogInformation("[{Timestamp}] METIS CMD=0x{Command:X2} LEN={Length} HEX={Hex}", timestamp, frame.Command, frame.Payload.Length, Convert.ToHexString(frame.RawFrame));
        }
    }
}
