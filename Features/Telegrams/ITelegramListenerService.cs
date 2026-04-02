using Yrki.IoT.WurthMetisII.Features.Arguments;

namespace Yrki.IoT.WurthMetisII.Features.Telegrams;

internal interface ITelegramListenerService
{
    Task ListenAsync(RuntimeOptions options, CancellationToken cancellationToken);
}
