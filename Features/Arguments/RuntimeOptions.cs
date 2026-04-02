namespace Yrki.IoT.WurthMetisII.Features.Arguments;

internal sealed record RuntimeOptions(
    string PortName,
    int BaudRate,
    bool Activate,
    string GatewayId,
    string MqttHost,
    int MqttPort,
    string MqttTopic,
    bool DumpParameters,
    bool ShowHelp);
