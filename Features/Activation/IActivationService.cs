using Yrki.IoT.WurthMetisII.Features.Arguments;

namespace Yrki.IoT.WurthMetisII.Features.Activation;

internal interface IActivationService
{
    void Activate(RuntimeOptions options, CancellationToken cancellationToken);
}
