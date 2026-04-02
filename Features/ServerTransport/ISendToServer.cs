namespace Yrki.IoT.WurthMetisII.Features.ServerTransport;

internal interface ISendToServer
{
    Task StartAsync(CancellationToken cancellationToken);
    Task SendAsync(ServerPayload payload, CancellationToken cancellationToken);
}
