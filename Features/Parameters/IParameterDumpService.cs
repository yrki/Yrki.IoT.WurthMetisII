using Yrki.IoT.WurthMetisII.Features.Arguments;

namespace Yrki.IoT.WurthMetisII.Features.Parameters;

internal interface IParameterDumpService
{
    void DumpAllParameters(RuntimeOptions options, CancellationToken cancellationToken);
}
