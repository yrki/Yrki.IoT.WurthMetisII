using Yrki.IoT.WurthMetisII.Features.MetisProtocol;
using Yrki.IoT.WurthMetisII.Features.ServerTransport;

namespace Yrki.IoT.WurthMetisII.Features.Telegrams;

internal interface IWMBusTelegramParserService
{
    ServerPayload? ParseAndPrint(string timestamp, MetisFrame frame, bool rssiEnabled, string gatewayId, string topic);
}
