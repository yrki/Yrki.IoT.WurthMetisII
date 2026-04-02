using MQTTnet;
using Microsoft.Extensions.Logging;
using Yrki.IoT.WurthMetisII.Features.Logging;

namespace Yrki.IoT.WurthMetisII.Features.ServerTransport;

internal sealed class SendToServerWithMqttService(
    ILogger<SendToServerWithMqttService> logger,
    IPayloadLogService payloadLogService) : ISendToServer, IAsyncDisposable
{
    private readonly IMqttClient _mqttClient = new MqttClientFactory().CreateMqttClient();
    private readonly MqttClientOptions _mqttOptions = new MqttClientOptionsBuilder()
        .WithTcpServer("localhost", 1883)
        .Build();

    private DateTimeOffset _nextReconnectAttemptUtc = DateTimeOffset.MinValue;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync("connected", cancellationToken);
    }

    public async Task SendAsync(ServerPayload payload, CancellationToken cancellationToken)
    {
        payloadLogService.Write(payload.PayloadHex);

        if (!await EnsureConnectedAsync("reconnected", cancellationToken))
        {
            return;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            payloadHex = payload.PayloadHex,
            gatewayId = payload.GatewayId,
            rssi = payload.Rssi,
            timestamp = payload.TimestampUtc.ToString("o")
        });

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(payload.Topic)
            .WithPayload(json)
            .Build();

        try
        {
            await _mqttClient.PublishAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MQTT publish failed");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync();
        }

        _mqttClient.Dispose();
    }

    private async Task<bool> EnsureConnectedAsync(string successVerb, CancellationToken cancellationToken)
    {
        if (_mqttClient.IsConnected)
        {
            return true;
        }

        if (DateTimeOffset.UtcNow < _nextReconnectAttemptUtc)
        {
            return false;
        }

        try
        {
            await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);
            logger.LogInformation("MQTT {SuccessVerb} to localhost:1883", successVerb);
            _nextReconnectAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MQTT connection failed");
            logger.LogInformation("Continuing without MQTT");
            _nextReconnectAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            return false;
        }
    }
}
