namespace Yrki.IoT.WurthMetisII.Features.Gateway;

internal sealed class GatewayIdService : IGatewayIdService
{
    public string GetGatewayId() => Environment.MachineName;
}
