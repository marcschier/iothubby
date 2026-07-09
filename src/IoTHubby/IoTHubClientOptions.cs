// Copyright (c) marcschier. Licensed under the MIT License.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Mqtt.Client;

namespace IoTHubby;

/// <summary>
/// Tunable options and dependency-injection seams for an IoT Hub device or module client. Every knob
/// has a sensible default; you only set what you need. Not thread-safe; configure before connecting.
/// </summary>
public sealed class IoTHubClientOptions
{
    /// <summary>
    /// Azure IoT Plug and Play model id advertised on connect (appended to the MQTT username as
    /// <c>model-id</c>). Optional.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Product info string appended to the MQTT username (<c>DeviceClientType</c>) for telemetry.
    /// Optional.
    /// </summary>
    public string? ProductInfo { get; set; }

    /// <summary>
    /// Timeout for a single request/response operation (twin get/update, method response send, DPS
    /// register/poll). Defaults to 30 seconds.
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Lifetime of a generated SAS token when authenticating with a symmetric key. Defaults to
    /// 1 hour. Ignored for X.509 or pre-computed-SAS authentication.
    /// </summary>
    public TimeSpan SasTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Fraction of <see cref="SasTokenLifetime"/> after which the client proactively renews the SAS
    /// token (by reconnecting with a fresh one) to avoid an expiry-driven disconnect. Defaults to
    /// 0.85. Clamped to the range (0, 1].
    /// </summary>
    public double SasTokenRenewalFraction { get; set; } = 0.85;

    /// <summary>
    /// When true (the default), the client automatically reconnects with exponential backoff after a
    /// dropped connection and re-establishes its subscriptions.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Initial reconnect backoff delay. Defaults to 500 ms.</summary>
    public TimeSpan ReconnectInitialDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum reconnect backoff delay. Defaults to 30 seconds.</summary>
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// MQTT keep-alive interval. Defaults to 60 seconds. Set to <see cref="TimeSpan.Zero"/> to
    /// disable keep-alive pings.
    /// </summary>
    public TimeSpan KeepAlive { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// X.509 client certificate for certificate-based authentication. Required when the connection
    /// string specifies <c>X509=true</c> (or use <c>CreateWithClientCertificate</c>).
    /// </summary>
    public X509Certificate2? ClientCertificate { get; set; }

    /// <summary>
    /// Hook to customize TLS options (e.g. a custom server-certificate validation callback for an
    /// IoT Edge gateway's CA trust bundle). Invoked when the TLS transport is built.
    /// </summary>
    public Action<SslClientAuthenticationOptions>? ConfigureTls { get; set; }

    /// <summary>
    /// Logger factory for diagnostic logging. Defaults to no logging.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Capacity of each inbound message channel (cloud-to-device, methods, desired updates, inputs).
    /// Defaults to 1024.
    /// </summary>
    public int ReceiveChannelCapacity { get; set; } = 1024;

    /// <summary>
    /// Internal seam: overrides the credentials used on every connect. Set by the IoTHubby.Edge
    /// package to present SAS tokens signed on demand by the Edge Workload API (with renewal). When
    /// set, it takes precedence over connection-string SAS/key material.
    /// </summary>
    internal IMqttCredentialsProvider? CredentialsProviderOverride { get; set; }

    /// <summary>Test-only seam: overrides the transport host (e.g. a loopback broker).</summary>
    internal string? EndpointHostOverride { get; set; }

    /// <summary>Test-only seam: overrides the transport port.</summary>
    internal int? EndpointPortOverride { get; set; }

    /// <summary>Test-only seam: connects over plain TCP instead of TLS (loopback broker).</summary>
    internal bool DisableTls { get; set; }
}
