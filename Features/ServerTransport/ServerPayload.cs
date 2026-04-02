namespace Yrki.IoT.WurthMetisII.Features.ServerTransport;

internal sealed record ServerPayload(
    string PayloadHex,
    string GatewayId,
    int? Rssi,
    DateTimeOffset TimestampUtc,
    string Topic);
