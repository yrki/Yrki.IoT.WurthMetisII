using Microsoft.Extensions.Logging;
using Yrki.IoT.WurthMetisII.Features.Arguments;
using Yrki.IoT.WurthMetisII.Features.MetisProtocol;
using Yrki.IoT.WurthMetisII.Features.Serial;

namespace Yrki.IoT.WurthMetisII.Features.Parameters;

internal sealed class ParameterDumpService(
    ILogger<ParameterDumpService> logger,
    ISerialPortService serialPortService,
    IMetisProtocolService metisProtocolService) : IParameterDumpService
{
    public void DumpAllParameters(RuntimeOptions options, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reading parameters 0x00..0x50");
        serialPortService.ConfigurePort(options.PortName, options.BaudRate);
        using var stream = serialPortService.OpenStream(options.PortName);
        var receiveBuffer = new List<byte>(256);

        for (byte param = 0x00; param <= 0x50; param++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = metisProtocolService.SendCommandAndGet(
                    stream,
                    receiveBuffer,
                    $"GET 0x{param:X2}",
                    metisProtocolService.BuildFrame(0x0A, [param, 0x01]),
                    0x8A,
                    timeoutMs: 500);

                if (response.Payload.Length >= 2)
                {
                    var payloadLength = response.Payload[1];
                    var values = response.Payload.AsSpan(2);
                    var dec = string.Join(", ", values.ToArray().Select(v => v.ToString()));
                    logger.LogInformation("[0x{Param:X2}] len={PayloadLength} hex={Hex} dec={DecimalValues}", param, payloadLength, metisProtocolService.ToHex(values), dec);
                }
                else
                {
                    logger.LogInformation("[0x{Param:X2}] raw={Raw}", param, metisProtocolService.ToHex(response.Payload));
                }
            }
            catch (TimeoutException)
            {
                logger.LogWarning("[0x{Param:X2}] no response", param);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "[0x{Param:X2}] error while reading parameter", param);
            }
        }
    }
}
