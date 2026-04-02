namespace Yrki.IoT.WurthMetisII.Features.Application;

internal interface IApplicationService
{
    Task<int> RunAsync(string[] args, CancellationToken cancellationToken);
}
