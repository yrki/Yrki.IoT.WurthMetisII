using Microsoft.Extensions.Logging;
using Yrki.IoT.WurthMetisII.Features.Arguments;
using Yrki.IoT.WurthMetisII.Features.MetisProtocol;
using Yrki.IoT.WurthMetisII.Features.Serial;

namespace Yrki.IoT.WurthMetisII.Features.Activation;

internal sealed class ActivationService(
    ILogger<ActivationService> logger,
    ISerialPortService serialPortService,
    IMetisProtocolService metisProtocolService) : IActivationService
{
    public void Activate(RuntimeOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Activating Metis-II Plug with minimal collector setup");

        serialPortService.ConfigurePort(options.PortName, options.BaudRate);
        using var serialStream = serialPortService.OpenStream(options.PortName);
        var receiveBuffer = new List<byte>(1024);

        metisProtocolService.SendCommandAndWait(serialStream, receiveBuffer, "CMD_SET_REQ UART_CMD_OUT_ENABLE=1", metisProtocolService.BuildFrame(0x09, [0x05, 0x01, 0x01]), 0x89);
        metisProtocolService.SendCommandAndWait(serialStream, receiveBuffer, "CMD_SET_REQ RSSI_ENABLE=1", metisProtocolService.BuildFrame(0x09, [0x45, 0x01, 0x01]), 0x89);
        metisProtocolService.SendCommandAndWait(serialStream, receiveBuffer, "CMD_SET_REQ MODE_PRESELECT=C2_T2_other", metisProtocolService.BuildFrame(0x09, [0x46, 0x01, 0x09]), 0x89);
        metisProtocolService.SendCommandAndPause(serialStream, receiveBuffer, "CMD_RESET_REQ", metisProtocolService.BuildFrame(0x05, []), 1200);

        serialPortService.ConfigurePort(options.PortName, options.BaudRate);
        using var runtimeStream = serialPortService.OpenStream(options.PortName);
        var runtimeBuffer = new List<byte>(1024);
        metisProtocolService.SendCommandAndWait(runtimeStream, runtimeBuffer, "CMD_SET_MODE_REQ C2_T2_other", metisProtocolService.BuildFrame(0x04, [0x09]), 0x84);
    }
}
