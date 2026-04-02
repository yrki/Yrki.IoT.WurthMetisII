using Yrki.IoT.WurthMetisII.Features.Arguments;

namespace Yrki.IoT.WurthMetisII.Features.ServerTransport;

internal interface ISendToServer
{
    Task StartAsync(RuntimeOptions options, CancellationToken cancellationToken);
    Task SendAsync(ServerPayload payload, CancellationToken cancellationToken);
}
