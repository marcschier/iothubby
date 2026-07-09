// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Mqtt.Client;

namespace IoTHubby.IntegrationTests;

/// <summary>
/// An in-process stand-in for the IoT Hub service side, implemented as a raw <see cref="MqttClient"/>
/// connected to the same <c>Mqtt.Client.Testing</c> broker as the device/module under test. It
/// answers twin GET/reported-PATCH requests (echoing the <c>$rid</c>), invokes and correlates direct
/// methods, captures telemetry, and can push cloud-to-device and edge-input messages — enough to
/// exercise the full client wire behaviour without a real hub.
/// </summary>
internal sealed class FakeIoTHub : IAsyncDisposable
{
    private readonly MqttClient _client;
    private readonly Channel<CapturedMessage> _telemetry =
        Channel.CreateUnbounded<CapturedMessage>(new UnboundedChannelOptions { SingleReader = false });
    private readonly ConcurrentDictionary<string, TaskCompletionSource<MethodResult>> _pendingMethods = new();

    private volatile string _twinJson = "{\"desired\":{\"$version\":1},\"reported\":{\"$version\":1}}";
    private int _reportedVersion = 1;
    private int _ridCounter;

    private FakeIoTHub(MqttClient client) => _client = client;

    /// <summary>Status code returned for twin GET (default 200); set to an error to exercise failures.</summary>
    public int TwinGetStatus { get; set; } = 200;

    /// <summary>The most recent reported-properties body received from the device.</summary>
    public string? LastReportedPatch { get; private set; }

    /// <summary>Starts a fake hub connected to the broker on the given loopback port.</summary>
    public static async Task<FakeIoTHub> StartAsync(int brokerPort)
    {
        var options = new MqttClientOptions
        {
            Host = "127.0.0.1",
            Port = brokerPort,
            Transport = MqttTransportType.Tcp,
            ProtocolVersion = MqttProtocolVersion.V311,
            ClientId = "fake-iot-hub",
            CleanStart = true,
        };
        var client = new MqttClient(options);
        var hub = new FakeIoTHub(client);
        await client.ConnectAsync().ConfigureAwait(false);

        await client.SubscribeAsync("$iothub/twin/GET/#", hub.OnTwinGetAsync).ConfigureAwait(false);
        await client.SubscribeAsync("$iothub/twin/PATCH/properties/reported/#", hub.OnReportedPatchAsync)
            .ConfigureAwait(false);
        await client.SubscribeAsync("$iothub/methods/res/#", hub.OnMethodResponseAsync).ConfigureAwait(false);
        await client.SubscribeAsync("devices/+/messages/events/#", hub.OnTelemetryAsync).ConfigureAwait(false);
        await client.SubscribeAsync("devices/+/modules/+/messages/events/#", hub.OnTelemetryAsync)
            .ConfigureAwait(false);
        return hub;
    }

    /// <summary>Sets the twin document returned for subsequent GET requests.</summary>
    public void SetTwin(string twinJson) => _twinJson = twinJson;

    /// <summary>Waits for the next telemetry message the device publishes.</summary>
    public async Task<CapturedMessage> WaitForTelemetryAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await _telemetry.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
    }

    /// <summary>Pushes a cloud-to-device message to the device.</summary>
    public Task PublishC2dAsync(string deviceId, string payload, string propertyBag = "")
        => _client.PublishAsync(
            $"devices/{deviceId}/messages/devicebound/{propertyBag}",
            Encoding.UTF8.GetBytes(payload),
            MqttQoS.AtLeastOnce,
            retain: false,
            properties: null,
            CancellationToken.None).AsTask();

    /// <summary>Pushes an edge input message to a module.</summary>
    public Task PublishInputAsync(
        string deviceId, string moduleId, string input, string payload, string propertyBag = "")
    {
        var suffix = string.IsNullOrEmpty(propertyBag) ? string.Empty : "/" + propertyBag;
        return _client.PublishAsync(
            $"devices/{deviceId}/modules/{moduleId}/inputs/{input}{suffix}",
            Encoding.UTF8.GetBytes(payload),
            MqttQoS.AtLeastOnce,
            retain: false,
            properties: null,
            CancellationToken.None).AsTask();
    }

    /// <summary>Pushes a desired-property update to the device.</summary>
    public Task PublishDesiredAsync(string desiredJson, long version)
    {
        var v = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return _client.PublishAsync(
            $"$iothub/twin/PATCH/properties/desired/?$version={v}",
            Encoding.UTF8.GetBytes(desiredJson),
            MqttQoS.AtLeastOnce,
            retain: false,
            properties: null,
            CancellationToken.None).AsTask();
    }

    /// <summary>Invokes a direct method on the device and awaits its response.</summary>
    public async Task<MethodResult> InvokeMethodAsync(string methodName, string payload, TimeSpan timeout)
    {
        var rid = Interlocked.Increment(ref _ridCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var tcs = new TaskCompletionSource<MethodResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingMethods[rid] = tcs;
        try
        {
            await _client.PublishAsync(
                $"$iothub/methods/POST/{methodName}/?$rid={rid}",
                Encoding.UTF8.GetBytes(payload),
                MqttQoS.AtLeastOnce,
                retain: false,
                properties: null,
                CancellationToken.None).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(timeout);
            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            _pendingMethods.TryRemove(rid, out _);
        }
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
            $"$iothub/twin/res/{TwinGetStatus.ToString(System.Globalization.CultureInfo.InvariantCulture)}/?$rid={rid}",
            Encoding.UTF8.GetBytes(_twinJson),
            MqttQoS.AtMostOnce,
            retain: false,
            properties: null,
            CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask OnReportedPatchAsync(MqttMessage message)
    {
        var rid = QueryValue(message.Topic, "$rid");
        LastReportedPatch = Encoding.UTF8.GetString(message.PayloadMemory.ToArray());
        message.Dispose();
        if (rid is null)
        {
            return;
        }
        var version = Interlocked.Increment(ref _reportedVersion);
        var v = version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await _client.PublishAsync(
            $"$iothub/twin/res/204/?$rid={rid}&$version={v}",
            ReadOnlyMemory<byte>.Empty,
            MqttQoS.AtMostOnce,
            retain: false,
            properties: null,
            CancellationToken.None).ConfigureAwait(false);
    }

    private ValueTask OnMethodResponseAsync(MqttMessage message)
    {
        var rid = QueryValue(message.Topic, "$rid");
        var status = StatusFromResponseTopic(message.Topic);
        var payload = message.PayloadMemory.ToArray();
        message.Dispose();
        if (rid is not null && _pendingMethods.TryRemove(rid, out var tcs))
        {
            tcs.TrySetResult(new MethodResult(status, payload));
        }
        return default;
    }

    private ValueTask OnTelemetryAsync(MqttMessage message)
    {
        _telemetry.Writer.TryWrite(new CapturedMessage(message.Topic, message.PayloadMemory.ToArray()));
        message.Dispose();
        return default;
    }

    private static int StatusFromResponseTopic(string topic)
    {
        // $iothub/methods/res/{status}/?$rid=...
        const string prefix = "$iothub/methods/res/";
        var rest = topic.AsSpan(prefix.Length);
        var slash = rest.IndexOf('/');
        var statusSpan = slash < 0 ? rest : rest.Slice(0, slash);
        return int.TryParse(statusSpan.ToString(), out var status) ? status : 0;
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

/// <summary>A telemetry message captured by the fake hub.</summary>
internal sealed record CapturedMessage(string Topic, byte[] Payload)
{
    public string PayloadString => Encoding.UTF8.GetString(Payload);
}

/// <summary>The outcome of a direct-method invocation observed by the fake hub.</summary>
internal readonly record struct MethodResult(int Status, byte[] Payload)
{
    public string PayloadString => Encoding.UTF8.GetString(Payload);
}
