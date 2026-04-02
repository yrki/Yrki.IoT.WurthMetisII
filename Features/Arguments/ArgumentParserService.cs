using Microsoft.Extensions.Logging;

namespace Yrki.IoT.WurthMetisII.Features.Arguments;

internal sealed class ArgumentParserService(ILogger<ArgumentParserService> logger) : IArgumentParserService
{
    public RuntimeOptions Parse(string[] args)
    {
        var portName = "/dev/cu.usbserial-53002FA7";
        var baudRate = 9600;
        var activate = false;
        var logFilePath = "payloads.log";
        var dumpParameters = false;
        var showHelp = false;

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
                case "--log-file" when i + 1 < args.Length:
                    logFilePath = args[++i];
                    break;
                case "--dump-params":
                    dumpParameters = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
            }
        }

        return new RuntimeOptions(portName, baudRate, activate, logFilePath, dumpParameters, showHelp);
    }

    public void PrintUsage()
    {
        logger.LogInformation("Usage: dotnet run -- [--port /dev/cu.usbserial-53002FA7] [--baud 9600] [--activate] [--dump-params] [--log-file payloads.log]");
    }
}
