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
    private static readonly TimeSpan SerialReconnectInterval = TimeSpan.FromSeconds(30);

    public async Task ListenAsync(RuntimeOptions options, CancellationToken cancellationToken)
    {
        var chunkBuffer = new byte[4096];

        logger.LogInformation("Listening for WMBus telegrams. Press Ctrl+C to stop.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                serialPortService.ConfigurePort(options.PortName, options.BaudRate);
                using var serialStream = serialPortService.OpenStream(options.PortName);
                logger.LogInformation("Serial connected to {PortName} at {BaudRate} baud", options.PortName, options.BaudRate);

                var rssiEnabled = TryReadRssiEnabled(serialStream);
                var metisBuffer = new List<byte>(8192);

                while (!cancellationToken.IsCancellationRequested)
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
                    await DumpParsedFramesAsync(metisBuffer, rssiEnabled, options.GatewayId, options.MqttTopic, cancellationToken);
                }
            }
            catch (IOException ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Serial connection to {PortName} lost", options.PortName);
                logger.LogInformation("Retrying serial connection in 30 seconds");
                await Task.Delay(SerialReconnectInterval, cancellationToken);
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

    private async Task DumpParsedFramesAsync(List<byte> receiveBuffer, bool rssiEnabled, string gatewayId, string topic, CancellationToken cancellationToken)
    {
        while (metisProtocolService.TryExtractMetisFrame(receiveBuffer, out var frame))
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");

            if (frame.Command == 0x03)
            {
                var payload = telegramParserService.ParseAndPrint(timestamp, frame, rssiEnabled, gatewayId, topic);
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
