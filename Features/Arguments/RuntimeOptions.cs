namespace Yrki.IoT.WurthMetisII.Features.Arguments;

internal sealed record RuntimeOptions(
    string PortName,
    int BaudRate,
    bool Activate,
    string LogFilePath,
    bool DumpParameters,
    bool ShowHelp);
