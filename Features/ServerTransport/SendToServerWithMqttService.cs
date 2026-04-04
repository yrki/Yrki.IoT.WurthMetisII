using MQTTnet;
using Microsoft.Extensions.Logging;
using Yrki.IoT.WurthMetisII.Features.Arguments;

namespace Yrki.IoT.WurthMetisII.Features.ServerTransport;

internal sealed class SendToServerWithMqttService(
    ILogger<SendToServerWithMqttService> logger) : ISendToServer, IAsyncDisposable
{
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(30);
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

        _mqttClient.DisconnectedAsync += args =>
        {
            if (args.Exception is not null)
            {
                logger.LogWarning(args.Exception, "MQTT disconnected from {Broker}", _brokerDescription);
            }
            else
            {
                logger.LogWarning("MQTT disconnected from {Broker}", _brokerDescription);
            }

            _nextReconnectAttemptUtc = DateTimeOffset.UtcNow.Add(ReconnectInterval);
            return Task.CompletedTask;
        };

        await EnsureConnectedAsync("connected", cancellationToken);
    }

    public async Task SendAsync(ServerPayload payload, CancellationToken cancellationToken)
    {
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
            _nextReconnectAttemptUtc = DateTimeOffset.UtcNow.Add(ReconnectInterval);

            if (_mqttClient.IsConnected)
            {
                try
                {
                    await _mqttClient.DisconnectAsync();
                }
                catch (Exception disconnectException)
                {
                    logger.LogDebug(disconnectException, "MQTT disconnect after publish failure also failed");
                }
            }
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
            _nextReconnectAttemptUtc = DateTimeOffset.UtcNow.Add(ReconnectInterval);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MQTT connection to {Broker} failed", _brokerDescription);
            logger.LogInformation("Continuing without MQTT");
            logger.LogInformation("Retrying MQTT connection in 30 seconds");
            _nextReconnectAttemptUtc = DateTimeOffset.UtcNow.Add(ReconnectInterval);
            return false;
        }
    }
}
