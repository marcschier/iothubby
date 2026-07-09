// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using Mqtt.Client;

namespace IoTHubby.ChaosTests;

internal sealed class FakeIoTHub : IAsyncDisposable
{
    private readonly MqttClient _client;
    private int _reportedVersion = 1;
    private long _telemetryReceived;
    private readonly string _twinJson = "{\"desired\":{\"$version\":1},\"reported\":{\"$version\":1}}";

    private FakeIoTHub(MqttClient client) => _client = client;

    public long TelemetryReceived => Volatile.Read(ref _telemetryReceived);

    public static async Task<FakeIoTHub> StartAsync(int brokerPort)
    {
        var client = new MqttClient(new MqttClientOptions
        {
            Host = "127.0.0.1",
            Port = brokerPort,
            Transport = MqttTransportType.Tcp,
            ProtocolVersion = MqttProtocolVersion.V311,
            ClientId = "fake-iot-hub",
            CleanStart = true,
            KeepAliveSeconds = 3,
            OperationTimeout = TimeSpan.FromSeconds(10),
            Reconnect = new MqttReconnectPolicy
            {
                InitialDelay = TimeSpan.FromMilliseconds(250),
                MaxDelay = TimeSpan.FromSeconds(2),
            },
        });

        var hub = new FakeIoTHub(client);
        await client.ConnectAsync().ConfigureAwait(false);
        await client.SubscribeAsync("$iothub/#", hub.OnIoTHubControlAsync).ConfigureAwait(false);
        await client.SubscribeAsync("devices/+/messages/events/#", hub.OnTelemetryAsync).ConfigureAwait(false);
        return hub;
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private ValueTask OnIoTHubControlAsync(MqttMessage message)
    {
        if (message.Topic.StartsWith("$iothub/twin/GET/", StringComparison.Ordinal))
        {
            return OnTwinGetAsync(message);
        }

        if (message.Topic.StartsWith("$iothub/twin/PATCH/properties/reported/", StringComparison.Ordinal))
        {
            return OnReportedPatchAsync(message);
        }

        message.Dispose();
        return default;
    }

    private async ValueTask OnTwinGetAsync(MqttMessage message)
    {
        var rid = QueryValue(message.Topic, "$rid");
        message.Dispose();
        if (rid is null)
        {
            return;
        }

        await _client.PublishAsync(
            $"$iothub/twin/res/200/?$rid={rid}",
            Encoding.UTF8.GetBytes(_twinJson),
            MqttQoS.AtMostOnce,
            retain: false,
            properties: null,
            CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask OnReportedPatchAsync(MqttMessage message)
    {
        var rid = QueryValue(message.Topic, "$rid");
        message.Dispose();
        if (rid is null)
        {
            return;
        }

        var version = Interlocked.Increment(ref _reportedVersion);
        await _client.PublishAsync(
            $"$iothub/twin/res/204/?$rid={rid}&$version={version}",
            ReadOnlyMemory<byte>.Empty,
            MqttQoS.AtMostOnce,
            retain: false,
            properties: null,
            CancellationToken.None).ConfigureAwait(false);
    }

    private ValueTask OnTelemetryAsync(MqttMessage message)
    {
        Interlocked.Increment(ref _telemetryReceived);
        message.Dispose();
        return default;
    }

    private static string? QueryValue(string topic, string key)
    {
        var queryIndex = topic.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            return null;
        }

        foreach (var pair in topic[(queryIndex + 1)..].Split('&'))
        {
            var equalsIndex = pair.IndexOf('=');
            if (equalsIndex > 0 && string.Equals(pair[..equalsIndex], key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair[(equalsIndex + 1)..]);
            }
        }

        return null;
    }
}
