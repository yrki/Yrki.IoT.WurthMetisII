namespace Yrki.IoT.WurthMetisII.Features.MetisProtocol;

internal interface IMetisProtocolService
{
    byte[] BuildFrame(byte command, byte[] payload);
    void SendCommandAndWait(
        FileStream serialStream,
        List<byte> receiveBuffer,
        string label,
        byte[] command,
        byte expectedCommand,
        int timeoutMs = 1500,
        int attempts = 5);
    MetisFrame SendCommandAndGet(
        FileStream serialStream,
        List<byte> receiveBuffer,
        string label,
        byte[] command,
        byte expectedCommand,
        int timeoutMs = 1500);
    void SendCommandAndPause(
        FileStream serialStream,
        List<byte> receiveBuffer,
        string label,
        byte[] command,
        int pauseMs);
    bool TryExtractMetisFrame(List<byte> receiveBuffer, out MetisFrame frame);
    string ToHex(ReadOnlySpan<byte> data);
}
