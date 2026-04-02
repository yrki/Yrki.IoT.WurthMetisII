using Microsoft.Extensions.Logging;
using Yrki.IoT.WurthMetisII.Features.Activation;
using Yrki.IoT.WurthMetisII.Features.Arguments;
using Yrki.IoT.WurthMetisII.Features.Logging;
using Yrki.IoT.WurthMetisII.Features.Parameters;
using Yrki.IoT.WurthMetisII.Features.Serial;
using Yrki.IoT.WurthMetisII.Features.ServerTransport;
using Yrki.IoT.WurthMetisII.Features.Telegrams;

namespace Yrki.IoT.WurthMetisII.Features.Application;

internal sealed class ApplicationService(
    ILogger<ApplicationService> logger,
    IArgumentParserService argumentParser,
    IPayloadLogService payloadLogService,
    ISerialPortService serialPortService,
    IActivationService activationService,
    IParameterDumpService parameterDumpService,
    ISendToServer sendToServer,
    ITelegramListenerService telegramListenerService) : IApplicationService
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = argumentParser.Parse(args);
        if (options.ShowHelp)
        {
            argumentParser.PrintUsage();
            return 0;
        }

        payloadLogService.Initialize(options.LogFilePath);

        logger.LogInformation("Opening {PortName} at {BaudRate} baud", options.PortName, options.BaudRate);
        serialPortService.ConfigurePort(options.PortName, options.BaudRate);

        if (options.Activate)
        {
            activationService.Activate(options, cancellationToken);
        }

        if (options.DumpParameters)
        {
            parameterDumpService.DumpAllParameters(options, cancellationToken);
            return 0;
        }

        await sendToServer.StartAsync(cancellationToken);
        await telegramListenerService.ListenAsync(options, cancellationToken);
        return 0;
    }
}
