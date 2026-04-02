namespace Yrki.IoT.WurthMetisII.Features.Logging;

internal interface IPayloadLogService
{
    void Initialize(string logFilePath);
    void Write(string payloadHex);
}
