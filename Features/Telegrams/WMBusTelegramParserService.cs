using Microsoft.Extensions.Logging;
using Yrki.IoT.WurthMetisII.Features.MetisProtocol;
using Yrki.IoT.WurthMetisII.Features.ServerTransport;

namespace Yrki.IoT.WurthMetisII.Features.Telegrams;

internal sealed class WMBusTelegramParserService(
    ILogger<WMBusTelegramParserService> logger) : IWMBusTelegramParserService
{
    public ServerPayload? ParseAndPrint(string timestamp, MetisFrame frame, bool rssiEnabled, string gatewayId, string topic)
    {
        var payload = frame.Payload;
        if (payload.Length < 11)
        {
            logger.LogInformation("[{Timestamp}] CMD_DATA_IND ({Length}B): {Hex}", timestamp, payload.Length, Convert.ToHexString(payload));
            return null;
        }

        int? rssiDbm = null;
        ReadOnlySpan<byte> wmbus;

        if (rssiEnabled && payload.Length >= 12)
        {
            var rssi = payload[^1];
            rssiDbm = rssi >= 128 ? (rssi - 256) / 2 - 74 : rssi / 2 - 74;
            wmbus = payload.AsSpan(0, payload.Length - 1);
        }
        else
        {
            wmbus = payload.AsSpan();
        }

        var lField = wmbus[0];
        var cField = wmbus[1];
        var mfr = (ushort)(wmbus[2] | (wmbus[3] << 8));
        var mfrStr = DecodeManufacturer(mfr);
        var id = DecodeBcd(wmbus.Slice(4, 4));
        var version = wmbus[8];
        var deviceType = wmbus[9];
        var rssiStr = rssiDbm.HasValue ? $" RSSI={rssiDbm}dBm" : string.Empty;

        logger.LogInformation(
            "[{Timestamp}] WMBus L={LField} C=0x{CField:X2} Mfr={Manufacturer} Id={DeviceId} Ver=0x{Version:X2} Dev=0x{DeviceType:X2}{RssiSuffix}",
            timestamp,
            lField,
            cField,
            mfrStr ?? "???",
            id ?? "????????",
            version,
            deviceType,
            rssiStr);

        if (wmbus.Length > 10)
        {
            var ciField = wmbus[10];
            logger.LogInformation("CI=0x{CiField:X2} AppData({Length}B): {Hex}", ciField, wmbus.Length - 11, Convert.ToHexString(wmbus[11..]));
        }

        logger.LogInformation("RAW({Length}B): {Hex}", wmbus.Length, Convert.ToHexString(wmbus));

        return new ServerPayload(
            Convert.ToHexString(wmbus),
            gatewayId,
            rssiDbm,
            DateTimeOffset.UtcNow,
            topic);
    }

    private static string? DecodeManufacturer(ushort code)
    {
        var a = (char)(((code >> 10) & 0x1F) + 64);
        var b = (char)(((code >> 5) & 0x1F) + 64);
        var c = (char)((code & 0x1F) + 64);
        return a is >= 'A' and <= 'Z' && b is >= 'A' and <= 'Z' && c is >= 'A' and <= 'Z'
            ? $"{a}{b}{c}"
            : null;
    }

    private static string? DecodeBcd(ReadOnlySpan<byte> bcd)
    {
        Span<char> chars = stackalloc char[bcd.Length * 2];
        var idx = 0;

        for (var i = bcd.Length - 1; i >= 0; i--)
        {
            var hi = (bcd[i] >> 4) & 0x0F;
            var lo = bcd[i] & 0x0F;
            if (hi > 9 || lo > 9)
            {
                return null;
            }

            chars[idx++] = (char)('0' + hi);
            chars[idx++] = (char)('0' + lo);
        }

        return new string(chars);
    }
}
