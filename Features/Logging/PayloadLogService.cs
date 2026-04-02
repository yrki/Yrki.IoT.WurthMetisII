using Microsoft.Extensions.Logging;

namespace Yrki.IoT.WurthMetisII.Features.Logging;

internal sealed class PayloadLogService(ILogger<PayloadLogService> logger) : IPayloadLogService, IDisposable
{
    private readonly object _lock = new();
    private StreamWriter? _writer;

    public void Initialize(string logFilePath)
    {
        var fullPath = Path.GetFullPath(logFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        lock (_lock)
        {
            _writer?.Dispose();
            _writer = new StreamWriter(new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
        }

        logger.LogInformation("Logging payloads to {LogFilePath}", fullPath);
    }

    public void Write(string payloadHex)
    {
        lock (_lock)
        {
            _writer?.WriteLine(payloadHex);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
