using MQTTnet;
using Microsoft.Extensions.Logging;
using Yrki.IoT.WurthMetisII.Features.Arguments;
using Yrki.IoT.WurthMetisII.Features.Logging;

namespace Yrki.IoT.WurthMetisII.Features.ServerTransport;

internal sealed class SendToServerWithMqttService(
    ILogger<SendToServerWithMqttService> logger,
    IPayloadLogService payloadLogService) : ISendToServer, IAsyncDisposable
{
    private readonly IMqttClient _mqttClient = new MqttClientFactory().CreateMqttClient();
    private MqttClientOptions? _mqttOptions;
    private string _brokerDescription = "";
    private DateTimeOffset _nextReconnectAttemptUtc = DateTimeOffset.MinValue;

    public async Task StartAsync(RuntimeOptions options, CancellationToken cancellationToken)
    {
        _brokerDescription = $"{options.MqttHost}:{options.MqttPort}";
        _mqttOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(options.MqttHost, options.MqttPort)
            .Build();

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

        if (_mqttOptions is null || DateTimeOffset.UtcNow < _nextReconnectAttemptUtc)
        {
            return false;
        }

        try
        {
            await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);
            logger.LogInformation("MQTT {SuccessVerb} to {Broker}", successVerb, _brokerDescription);
            _nextReconnectAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MQTT connection to {Broker} failed", _brokerDescription);
            logger.LogInformation("Continuing without MQTT");
            _nextReconnectAttemptUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            return false;
        }
    }
}
