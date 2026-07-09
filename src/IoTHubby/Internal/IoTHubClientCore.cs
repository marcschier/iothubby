// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Runtime.CompilerServices;
using Mqtt.Client;

namespace IoTHubby;

/// <summary>
/// The shared engine behind <see cref="IoTHubDeviceClient"/> and <see cref="IoTHubModuleClient"/>.
/// Owns the session, the well-known subscriptions, the <c>$rid</c> correlation for twin operations,
/// and the telemetry/C2D/method/twin data paths. Subscriptions are established lazily on first use
/// and are re-established automatically by the underlying client after a reconnect.
/// </summary>
internal sealed class IoTHubClientCore : IAsyncDisposable
{
    private readonly IoTHubConnectionString _connection;
    private readonly IoTHubTopics _topics;
    private readonly IoTHubClientOptions _options;
    private readonly IoTHubSession _session;
    private readonly RidCorrelator _twinRids = new();

    private readonly SemaphoreSlim _twinGate = new(1, 1);
    private readonly SemaphoreSlim _methodGate = new(1, 1);

    private MqttSubscription? _twinResSub;
    private Task? _twinPump;

    private MqttSubscription? _methodSub;
    private Task? _methodPump;
    private Func<DirectMethodRequest, CancellationToken, ValueTask<DirectMethodResponse>>? _methodHandler;

    private volatile bool _disposed;

    public IoTHubClientCore(IoTHubConnectionString connection, IoTHubTopics topics, IoTHubClientOptions options)
    {
        _connection = connection;
        _topics = topics;
        _options = options;
        _session = IoTHubSession.Create(connection, topics, options);
        _session.ConnectionStateChanged += (_, e) => ConnectionStateChanged?.Invoke(this, e);
    }

    public event EventHandler<IoTHubConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public IoTHubConnectionState State => _session.State;

    public Task ConnectAsync(CancellationToken cancellationToken) => _session.ConnectAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) => _session.DisconnectAsync(cancellationToken);

    // ───────────────────────────── Telemetry (D2C / module output) ─────────────────────────────

    public async ValueTask SendTelemetryAsync(
        TelemetryMessage message,
        string? outputName,
        CancellationToken cancellationToken)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var topic = IoTHubWireCodec.BuildTelemetryTopic(_topics, message, outputName);
        var result = await _session.PublishAsync(topic, message.Payload, message.QoS, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new IoTHubClientException(
                $"Telemetry publish was rejected (reason code 0x{(byte)result.ReasonCode:X2}).");
        }
    }

    // ───────────────────────────── Cloud-to-device / edge inputs ───────────────────────────────

    public async IAsyncEnumerable<CloudToDeviceMessage> ReceiveCloudToDeviceMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sub = await _session.SubscribeAsync(
            _topics.CloudToDeviceSubscribe,
            IoTHubQoS.AtLeastOnce,
            _options.ReceiveChannelCapacity,
            cancellationToken)
            .ConfigureAwait(false);
        await using (sub.ConfigureAwait(false))
        {
            await foreach (var msg in sub.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return MapInbound(msg, _topics.CloudToDevicePrefix);
            }
        }
    }

    public async IAsyncEnumerable<CloudToDeviceMessage> ReceiveInputMessagesAsync(
        string inputName, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_topics.InputSubscribe is null || _topics.InputPrefix is null)
        {
            throw new InvalidOperationException("Input messages are only available on a module client.");
        }

        var sub = await _session.SubscribeAsync(
            _topics.InputSubscribe,
            IoTHubQoS.AtLeastOnce,
            _options.ReceiveChannelCapacity,
            cancellationToken)
            .ConfigureAwait(false);
        await using (sub.ConfigureAwait(false))
        {
            await foreach (var msg in sub.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                using (msg)
                {
                    if (!TopicParser.TryParseInput(msg.Topic, _topics.InputPrefix, out var input, out var bag))
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(inputName) && !string.Equals(input, inputName, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    yield return MapMessage(msg.PayloadMemory, bag.AsSpan(), input);
                }
            }
        }
    }

    private static CloudToDeviceMessage MapInbound(MqttMessage msg, string prefix)
    {
        using (msg)
        {
            return IoTHubWireCodec.MapInbound(msg.Topic, prefix, msg.PayloadMemory);
        }
    }

    private static CloudToDeviceMessage MapMessage(
        ReadOnlyMemory<byte> payload,
        ReadOnlySpan<char> bag,
        string? inputName)
        => IoTHubWireCodec.MapMessage(payload, bag, inputName);

    // ───────────────────────────── Direct methods ──────────────────────────────────────────────

    public void SetMethodHandler(Func<DirectMethodRequest, CancellationToken, ValueTask<DirectMethodResponse>>? handler)
        => _methodHandler = handler;

    public async Task EnsureMethodSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (_methodSub is not null)
        {
            return;
        }
        await _methodGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_methodSub is not null)
            {
                return;
            }
            var sub = await _session.SubscribeAsync(
                IoTHubTopics.MethodSubscribe,
                IoTHubQoS.AtLeastOnce,
                _options.ReceiveChannelCapacity,
                cancellationToken)
                .ConfigureAwait(false);
            _methodPump = Task.Run(() => PumpMethodsAsync(sub), CancellationToken.None);
            _methodSub = sub;
        }
        finally
        {
            _methodGate.Release();
        }
    }

    private async Task PumpMethodsAsync(MqttSubscription sub)
    {
        await foreach (var msg in sub.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            string name, rid;
            byte[] payload;
            using (msg)
            {
                if (!TopicParser.TryParseMethodRequest(msg.Topic, out name, out rid))
                {
                    continue;
                }
                payload = msg.PayloadMemory.ToArray();
            }

            var handler = _methodHandler;
            DirectMethodResponse response;
            try
            {
                response = handler is null
                    ? DirectMethodResponse.FromStatus(404)
                    : await handler(
                        new DirectMethodRequest(name, new ReadOnlySequence<byte>(payload)),
                        CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                response = DirectMethodResponse.FromStatus(500);
            }

            try
            {
                await _session.PublishAsync(
                    IoTHubTopics.MethodResponse(response.Status, rid),
                    response.Payload,
                    IoTHubQoS.AtLeastOnce,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) when (!_disposed)
            {
                // Best-effort response; a dropped connection will be recovered by auto-reconnect.
            }
        }
    }

    // ───────────────────────────── Twin ────────────────────────────────────────────────────────

    public async Task<Twin> GetTwinAsync(CancellationToken cancellationToken)
    {
        await EnsureTwinSubscriptionAsync(cancellationToken).ConfigureAwait(false);
        var rid = _twinRids.Next();
        var wait = _twinRids.WaitAsync(rid, _options.OperationTimeout, cancellationToken);
        await _session.PublishAsync(
            IoTHubTopics.TwinGet(rid),
            ReadOnlyMemory<byte>.Empty,
            IoTHubQoS.AtLeastOnce,
            cancellationToken)
            .ConfigureAwait(false);
        var response = await wait.ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new IoTHubClientException($"GetTwin failed with status {response.Status}.", response.Status);
        }
        return IoTHubWireCodec.ParseTwin(response.Payload);
    }

    public async Task<long?> UpdateReportedPropertiesAsync(
        ReadOnlySequence<byte> reportedJson,
        CancellationToken cancellationToken)
    {
        await EnsureTwinSubscriptionAsync(cancellationToken).ConfigureAwait(false);
        var rid = _twinRids.Next();
        var wait = _twinRids.WaitAsync(rid, _options.OperationTimeout, cancellationToken);
        await _session.PublishAsync(
            IoTHubTopics.TwinReportedPatch(rid),
            reportedJson,
            IoTHubQoS.AtLeastOnce,
            cancellationToken)
            .ConfigureAwait(false);
        var response = await wait.ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new IoTHubClientException(
                $"UpdateReportedProperties failed with status {response.Status}.",
                response.Status);
        }
        return response.Query.TryGetValue("$version", out var v)
            && long.TryParse(
                v,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var version)
            ? version
            : null;
    }

    public async IAsyncEnumerable<DesiredPropertyUpdate> ReceiveDesiredPropertyUpdatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sub = await _session.SubscribeAsync(
            IoTHubTopics.TwinDesiredSubscribe,
            IoTHubQoS.AtLeastOnce,
            _options.ReceiveChannelCapacity,
            cancellationToken)
            .ConfigureAwait(false);
        await using (sub.ConfigureAwait(false))
        {
            await foreach (var msg in sub.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                DesiredPropertyUpdate update;
                using (msg)
                {
                    var version = TopicParser.ParseDesiredVersion(msg.Topic);
                    update = new DesiredPropertyUpdate(new TwinProperties(msg.PayloadMemory.ToArray(), version));
                }
                yield return update;
            }
        }
    }

    private async Task EnsureTwinSubscriptionAsync(CancellationToken cancellationToken)
    {
        if (_twinResSub is not null)
        {
            return;
        }
        await _twinGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_twinResSub is not null)
            {
                return;
            }
            var sub = await _session.SubscribeAsync(
                IoTHubTopics.TwinResponseSubscribe,
                IoTHubQoS.AtLeastOnce,
                _options.ReceiveChannelCapacity,
                cancellationToken)
                .ConfigureAwait(false);
            _twinPump = Task.Run(() => PumpTwinResponsesAsync(sub), CancellationToken.None);
            _twinResSub = sub;
        }
        finally
        {
            _twinGate.Release();
        }
    }

    private async Task PumpTwinResponsesAsync(MqttSubscription sub)
    {
        await foreach (var msg in sub.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            using (msg)
            {
                if (TopicParser.TryParseTwinResponse(
                        msg.Topic,
                        out var status,
                        out var rid,
                        out var query)
                    && rid.Length > 0)
                {
                    _twinRids.TryComplete(rid, status, msg.PayloadMemory.Span, query);
                }
            }
        }
    }

    private static Twin ParseTwin(byte[] json) => IoTHubWireCodec.ParseTwin(json);

    // ───────────────────────────── Lifetime ────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_methodSub is not null)
        {
            await _methodSub.DisposeAsync().ConfigureAwait(false);
        }
        if (_twinResSub is not null)
        {
            await _twinResSub.DisposeAsync().ConfigureAwait(false);
        }
        await SafeAwait(_methodPump).ConfigureAwait(false);
        await SafeAwait(_twinPump).ConfigureAwait(false);

        _twinRids.FailAll(new ObjectDisposedException(nameof(IoTHubClientCore)));
        await _session.DisposeAsync().ConfigureAwait(false);
        _twinGate.Dispose();
        _methodGate.Dispose();
    }

    private static async Task SafeAwait(Task? task)
    {
        if (task is null)
        {
            return;
        }
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Pump loops end when their subscription channel completes; ignore teardown races.
        }
    }
}
