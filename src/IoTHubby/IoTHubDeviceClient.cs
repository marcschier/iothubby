// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IoTHubby;

/// <summary>
/// A device-side client for Azure IoT Hub over MQTT. Connect once and use — reconnection, SAS token
/// renewal, and re-subscription are handled automatically.
/// </summary>
/// <example>
/// <code>
/// await using var client = IoTHubDeviceClient.CreateFromConnectionString(cs);
/// await client.ConnectAsync();
/// await client.SendTelemetryAsync(TelemetryMessage.FromString("{\"t\":21}"));
/// </code>
/// </example>
public sealed class IoTHubDeviceClient : IAsyncDisposable
{
    private readonly IoTHubClientCore _core;

    private IoTHubDeviceClient(IoTHubClientCore core)
    {
        _core = core;
        _core.ConnectionStateChanged += (_, e) => ConnectionStateChanged?.Invoke(this, e);
    }

    /// <summary>Raised on every connection-state transition.</summary>
    public event EventHandler<IoTHubConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>The current connection state.</summary>
    public IoTHubConnectionState State => _core.State;

    /// <summary>
    /// Creates a device client from an IoT Hub device connection string
    /// (<c>HostName=...;DeviceId=...;SharedAccessKey=...</c> or <c>...;X509=true</c>).
    /// </summary>
    public static IoTHubDeviceClient CreateFromConnectionString(
        string connectionString, Action<IoTHubClientOptions>? configure = null)
    {
        var connection = IoTHubConnectionString.Parse(connectionString);
        if (connection.IsModule)
        {
            throw new ArgumentException(
                "This connection string is for a module; use IoTHubModuleClient.", nameof(connectionString));
        }
        var options = new IoTHubClientOptions();
        configure?.Invoke(options);
        var topics = new IoTHubTopics(connection.DeviceId);
        return new IoTHubDeviceClient(new IoTHubClientCore(connection, topics, options));
    }

    /// <summary>Creates a device client that authenticates with an X.509 client certificate.</summary>
    public static IoTHubDeviceClient CreateWithClientCertificate(
        string hostName, string deviceId, X509Certificate2 certificate, Action<IoTHubClientOptions>? configure = null)
    {
        if (certificate is null)
        {
            throw new ArgumentNullException(nameof(certificate));
        }
        var connection = IoTHubConnectionString.Parse($"HostName={hostName};DeviceId={deviceId};X509=true");
        var options = new IoTHubClientOptions { ClientCertificate = certificate };
        configure?.Invoke(options);
        var topics = new IoTHubTopics(connection.DeviceId);
        return new IoTHubDeviceClient(new IoTHubClientCore(connection, topics, options));
    }

    /// <summary>Connects to IoT Hub. Throws <see cref="IoTHubClientException"/> if the hub rejects it.</summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default) => _core.ConnectAsync(cancellationToken);

    /// <summary>Disconnects gracefully.</summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _core.DisconnectAsync(cancellationToken);

    /// <summary>Sends a device-to-cloud telemetry message.</summary>
    public ValueTask SendTelemetryAsync(TelemetryMessage message, CancellationToken cancellationToken = default)
        => _core.SendTelemetryAsync(message, outputName: null, cancellationToken);

    /// <summary>
    /// Streams cloud-to-device messages until the enumeration is disposed or cancelled. Each message
    /// is completed (acknowledged) automatically as it is delivered.
    /// </summary>
    public IAsyncEnumerable<CloudToDeviceMessage> ReceiveCloudToDeviceMessagesAsync(
        CancellationToken cancellationToken = default)
        => _core.ReceiveCloudToDeviceMessagesAsync(cancellationToken);

    /// <summary>
    /// Registers the handler invoked for every inbound direct-method call and subscribes to method
    /// requests. Pass <c>null</c> to stop handling (subsequent calls return 404).
    /// </summary>
    public async Task SetMethodHandlerAsync(
        Func<DirectMethodRequest, CancellationToken, ValueTask<DirectMethodResponse>>? handler,
        CancellationToken cancellationToken = default)
    {
        _core.SetMethodHandler(handler);
        if (handler is not null)
        {
            await _core.EnsureMethodSubscriptionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Gets the device twin (desired and reported properties).</summary>
    public Task<Twin> GetTwinAsync(CancellationToken cancellationToken = default)
        => _core.GetTwinAsync(cancellationToken);

    /// <summary>Patches reported properties from raw JSON bytes. Returns the new twin version.</summary>
    public Task<long?> UpdateReportedPropertiesAsync(
        ReadOnlyMemory<byte> reportedJson, CancellationToken cancellationToken = default)
        => _core.UpdateReportedPropertiesAsync(reportedJson, cancellationToken);

    /// <summary>Patches reported properties from a JSON string. Returns the new twin version.</summary>
    public Task<long?> UpdateReportedPropertiesAsync(string reportedJson, CancellationToken cancellationToken = default)
        => _core.UpdateReportedPropertiesAsync(
            System.Text.Encoding.UTF8.GetBytes(reportedJson ?? "{}"), cancellationToken);

    /// <summary>
    /// Patches reported properties from a strongly-typed value, serialized via a source-generated
    /// <paramref name="typeInfo"/> (AOT/trim safe). Returns the new twin version.
    /// </summary>
    public Task<long?> UpdateReportedPropertiesAsync<T>(
        T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
        => _core.UpdateReportedPropertiesAsync(
            JsonSerializer.SerializeToUtf8Bytes(value, typeInfo), cancellationToken);

    /// <summary>Streams desired-property updates pushed by the service.</summary>
    public IAsyncEnumerable<DesiredPropertyUpdate> ReceiveDesiredPropertyUpdatesAsync(
        CancellationToken cancellationToken = default)
        => _core.ReceiveDesiredPropertyUpdatesAsync(cancellationToken);

    /// <summary>Disconnects and releases all resources.</summary>
    public ValueTask DisposeAsync() => _core.DisposeAsync();
}
