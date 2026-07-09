// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using Mqtt.Client;

namespace IoTHubby.IntegrationTests;

/// <summary>
/// An in-process stand-in for the Device Provisioning Service, connected to the same broker as the
/// <c>ProvisioningClient</c> under test. It answers a register (<c>PUT</c>) with a <c>202 assigning</c>
/// carrying an operation id and a short retry-after, then answers the status poll (<c>GET</c>) with a
/// <c>200 assigned</c> and a registration state.
/// </summary>
internal sealed class FakeDps : IAsyncDisposable
{
    private readonly MqttClient _client;
    private readonly string _assignedHub;
    private readonly string _deviceId;

    private FakeDps(MqttClient client, string assignedHub, string deviceId)
    {
        _client = client;
        _assignedHub = assignedHub;
        _deviceId = deviceId;
    }

    public static async Task<FakeDps> StartAsync(int brokerPort, string assignedHub, string deviceId)
    {
        var options = new MqttClientOptions
        {
            Host = "127.0.0.1",
            Port = brokerPort,
            Transport = MqttTransportType.Tcp,
            ProtocolVersion = MqttProtocolVersion.V311,
            ClientId = "fake-dps",
            CleanStart = true,
        };
        var client = new MqttClient(options);
        var dps = new FakeDps(client, assignedHub, deviceId);
        await client.ConnectAsync().ConfigureAwait(false);
        await client.SubscribeAsync("$dps/registrations/PUT/#", dps.OnRegisterAsync).ConfigureAwait(false);
        await client.SubscribeAsync("$dps/registrations/GET/#", dps.OnPollAsync).ConfigureAwait(false);
        return dps;
    }

    private async ValueTask OnRegisterAsync(MqttMessage message)
    {
        var rid = QueryValue(message.Topic, "$rid");
        message.Dispose();
        if (rid is null)
        {
            return;
        }
        const string body = "{\"operationId\":\"op-1\",\"status\":\"assigning\"}";
        await _client.PublishAsync(
            $"$dps/registrations/res/202/?$rid={rid}&retry-after=1",
            Encoding.UTF8.GetBytes(body),
            MqttQoS.AtMostOnce,
            retain: false,
            properties: null,
            CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask OnPollAsync(MqttMessage message)
    {
        var rid = QueryValue(message.Topic, "$rid");
        message.Dispose();
        if (rid is null)
        {
            return;
        }
        var body =
            "{\"operationId\":\"op-1\",\"status\":\"assigned\",\"registrationState\":{" +
            $"\"assignedHub\":\"{_assignedHub}\",\"deviceId\":\"{_deviceId}\",\"status\":\"assigned\"}}}}";
        await _client.PublishAsync(
            $"$dps/registrations/res/200/?$rid={rid}",
            Encoding.UTF8.GetBytes(body),
            MqttQoS.AtMostOnce,
            retain: false,
            properties: null,
            CancellationToken.None).ConfigureAwait(false);
    }

    private static string? QueryValue(string topic, string key)
    {
        var q = topic.IndexOf('?');
        if (q < 0)
        {
            return null;
        }
        foreach (var pair in topic.Substring(q + 1).Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair.Substring(0, eq) == key)
            {
                return pair.Substring(eq + 1);
            }
        }
        return null;
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync().ConfigureAwait(false);
}
