// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Mqtt.Client;

namespace IoTHubby;

/// <summary>
/// Owns the underlying <see cref="MqttClient"/> for one device/module identity and adapts it to the
/// IoT Hub connection model: MQTT 3.1.1 over TLS on port 8883, SAS/X.509 authentication with
/// automatic token renewal, automatic reconnect, and automatic re-subscription. Feature layers
/// (telemetry, C2D, methods, twin) publish and subscribe through this session.
/// </summary>
internal sealed class IoTHubSession : IAsyncDisposable
{
    private readonly MqttClient _client;
    private readonly IDisposable? _ownedCredentials;

    private IoTHubSession(MqttClient client, IDisposable? ownedCredentials)
    {
        _client = client;
        _ownedCredentials = ownedCredentials;
        _client.StateChanged += OnStateChanged;
        _client.Disconnected += OnDisconnected;
    }

    /// <summary>Raised on every connection-state transition.</summary>
    public event EventHandler<IoTHubConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>The current connection state.</summary>
    public IoTHubConnectionState State => (IoTHubConnectionState)_client.State;

    /// <summary>Builds a session for the given identity, topics, and options.</summary>
    public static IoTHubSession Create(
        IoTHubConnectionString connection,
        IoTHubTopics topics,
        IoTHubClientOptions options)
    {
        var host = connection.HostName;              // audience/username host is always the hub host
        var connectHost = connection.ConnectHost;    // transport host may be the edge gateway
        var username = topics.BuildUsername(host, options.ProductInfo, options.ModelId);

        // Test seam: point the transport at a loopback broker over plain TCP. Never used in production.
        var transportHost = string.IsNullOrEmpty(options.EndpointHostOverride)
            ? connectHost
            : options.EndpointHostOverride!;
        var port = options.EndpointPortOverride ?? IoTHubProtocol.SecureMqttPort;
        var transport = options.DisableTls ? MqttTransportType.Tcp : MqttTransportType.Tls;

        var mqtt = new MqttClientOptions
        {
            Host = transportHost,
            Port = port,
            Transport = transport,
            ProtocolVersion = MqttProtocolVersion.V311,
            ClientId = topics.ClientId,
            CleanStart = true,
            KeepAliveSeconds = (ushort)Math.Max(0, options.KeepAlive.TotalSeconds),
            OperationTimeout = options.OperationTimeout,
            Reconnect = options.AutoReconnect
                ? new MqttReconnectPolicy
                {
                    InitialDelay = options.ReconnectInitialDelay,
                    MaxDelay = options.ReconnectMaxDelay,
                }
                : null,
        };

        var tls = new SslClientAuthenticationOptions { TargetHost = connectHost };

        SasCredentialsProvider? sasProvider = null;
        IDisposable? ownedCredentials = null;
        if (options.CredentialsProviderOverride is { } overrideProvider)
        {
            // Edge / custom path: credentials are supplied (and renewed) externally. The client owns
            // the override for its lifetime, so dispose it here if it holds resources (e.g. the edge
            // workload provider's HttpClient + renewal timer).
            mqtt.CredentialsProvider = overrideProvider;
            ownedCredentials = overrideProvider as IDisposable;
        }
        else
        {
            switch (connection.AuthMethod)
            {
                case IoTHubAuthMethod.SharedAccessKey:
                    var resourceUri = connection.IsModule
                        ? SasTokenGenerator.ModuleResourceUri(host, connection.DeviceId, connection.ModuleId!)
                        : SasTokenGenerator.DeviceResourceUri(host, connection.DeviceId);
                    sasProvider = new SasCredentialsProvider(
                        username,
                        resourceUri,
                        connection.SharedAccessKey!,
                        options.SasTokenLifetime,
                        options.SasTokenRenewalFraction);
                    mqtt.CredentialsProvider = sasProvider;
                    break;

                case IoTHubAuthMethod.SharedAccessSignature:
                    mqtt.Username = username;
                    mqtt.Password = Encoding.UTF8.GetBytes(connection.SharedAccessSignature!);
                    break;

                case IoTHubAuthMethod.X509:
                    mqtt.Username = username;
                    var certificate = options.ClientCertificate
                        ?? throw new InvalidOperationException(
                            "X.509 authentication requires IoTHubClientOptions.ClientCertificate to be set.");
                    tls.ClientCertificates = new X509CertificateCollection { certificate };
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(connection));
            }
        }

        options.ConfigureTls?.Invoke(tls);
        mqtt.Tls = tls;

        var client = new MqttClient(mqtt, options.LoggerFactory);
        return new IoTHubSession(client, ownedCredentials ?? sasProvider);
    }

    /// <summary>Connects (or fails). Auto-reconnect then keeps the connection alive.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var result = await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new IoTHubClientException(
                $"IoT Hub rejected the connection (reason code 0x{(byte)result.ReasonCode:X2}).");
        }
    }

    /// <summary>Publishes a message to <paramref name="topic"/> at the given QoS.</summary>
    public ValueTask<MqttPublishResult> PublishAsync(
        string topic,
        ReadOnlyMemory<byte> payload,
        IoTHubQoS qos,
        CancellationToken cancellationToken)
        => _client.PublishAsync(topic, payload, (MqttQoS)qos, retain: false, properties: null, cancellationToken);

    /// <summary>Subscribes to <paramref name="topicFilter"/> and returns the channel-backed subscription.</summary>
    public ValueTask<MqttSubscription> SubscribeAsync(
        string topicFilter,
        IoTHubQoS qos,
        int capacity,
        CancellationToken cancellationToken)
        => _client.SubscribeAsync(
            topicFilter,
            new MqttSubscriptionOptions { QoS = (MqttQoS)qos, Capacity = capacity },
            cancellationToken);

    /// <summary>Disconnects gracefully.</summary>
    public Task DisconnectAsync(CancellationToken cancellationToken)
        => _client.DisconnectAsync(cancellationToken);

    private void OnStateChanged(object? sender, MqttConnectionState state)
        => ConnectionStateChanged?.Invoke(
            this,
            new IoTHubConnectionStateChangedEventArgs((IoTHubConnectionState)state));

    private void OnDisconnected(object? sender, MqttDisconnectedEventArgs e)
        => ConnectionStateChanged?.Invoke(
            this,
            new IoTHubConnectionStateChangedEventArgs(IoTHubConnectionState.Disconnected, e.Reason));

    public async ValueTask DisposeAsync()
    {
        _client.StateChanged -= OnStateChanged;
        _client.Disconnected -= OnDisconnected;
        await _client.DisposeAsync().ConfigureAwait(false);
        _ownedCredentials?.Dispose();
    }
}
