// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IoTHubby;

/// <summary>
/// A module-side client for Azure IoT Hub and Azure IoT Edge over MQTT. In addition to the device
/// capabilities it supports edge module-to-module messaging (named outputs and inputs). Connect once
/// and use — reconnection, SAS renewal, and re-subscription are automatic.
/// </summary>
public sealed class IoTHubModuleClient : IAsyncDisposable, IIoTHubConnectableClient
{
    private readonly IoTHubClientCore _core;

    private IoTHubModuleClient(IoTHubClientCore core)
    {
        _core = core;
        _core.ConnectionStateChanged += (_, e) => ConnectionStateChanged?.Invoke(this, e);
    }

    /// <summary>Raised on every connection-state transition.</summary>
    public event EventHandler<IoTHubConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>The current connection state.</summary>
    public IoTHubConnectionState State => _core.State;

    /// <summary>
    /// Creates a module client from an IoT Hub / IoT Edge module connection string
    /// (<c>HostName=...;DeviceId=...;ModuleId=...;SharedAccessKey=...</c>, optionally with
    /// <c>GatewayHostName=...</c> for an edge gateway).
    /// </summary>
    public static IoTHubModuleClient CreateFromConnectionString(
        string connectionString, Action<IoTHubClientOptions>? configure = null)
    {
        var connection = IoTHubConnectionString.Parse(connectionString);
        if (!connection.IsModule)
        {
            throw new ArgumentException(
                "This connection string is for a device; use IoTHubDeviceClient.", nameof(connectionString));
        }
        var options = new IoTHubClientOptions();
        configure?.Invoke(options);
        return Create(connection, options);
    }

    /// <summary>Creates a module client that authenticates with an X.509 client certificate.</summary>
    public static IoTHubModuleClient CreateWithClientCertificate(
        string hostName,
        string deviceId,
        string moduleId,
        X509Certificate2 certificate,
        Action<IoTHubClientOptions>? configure = null)
    {
        if (certificate is null)
        {
            throw new ArgumentNullException(nameof(certificate));
        }
        var connection = IoTHubConnectionString.Parse(
            $"HostName={hostName};DeviceId={deviceId};ModuleId={moduleId};X509=true");
        var options = new IoTHubClientOptions { ClientCertificate = certificate };
        configure?.Invoke(options);
        return Create(connection, options);
    }

    internal static IoTHubModuleClient Create(IoTHubConnectionString connection, IoTHubClientOptions options)
    {
        var topics = new IoTHubTopics(connection.DeviceId, connection.ModuleId);
        return new IoTHubModuleClient(new IoTHubClientCore(connection, topics, options));
    }

    /// <summary>Connects to IoT Hub / the edge gateway.</summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default) => _core.ConnectAsync(cancellationToken);

    /// <summary>Disconnects gracefully.</summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _core.DisconnectAsync(cancellationToken);

    /// <summary>Sends a module-to-cloud telemetry message.</summary>
    public ValueTask SendTelemetryAsync(TelemetryMessage message, CancellationToken cancellationToken = default)
        => _core.SendTelemetryAsync(message, outputName: null, cancellationToken);

    /// <summary>Sends a message to a named edge output route (module-to-module).</summary>
    public ValueTask SendToOutputAsync(
        string outputName, TelemetryMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(outputName))
        {
            throw new ArgumentException("Output name is required.", nameof(outputName));
        }
        return _core.SendTelemetryAsync(message, outputName, cancellationToken);
    }

    /// <summary>Streams cloud-to-module (C2D) messages.</summary>
    public IAsyncEnumerable<CloudToDeviceMessage> ReceiveCloudToDeviceMessagesAsync(
        CancellationToken cancellationToken = default)
        => _core.ReceiveCloudToDeviceMessagesAsync(cancellationToken);

    /// <summary>
    /// Streams messages arriving on the given edge input route. Pass an empty string to receive from
    /// all inputs.
    /// </summary>
    public IAsyncEnumerable<CloudToDeviceMessage> ReceiveInputMessagesAsync(
        string inputName, CancellationToken cancellationToken = default)
        => _core.ReceiveInputMessagesAsync(inputName, cancellationToken);

    /// <summary>Registers the direct-method handler and subscribes to method requests.</summary>
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

    /// <summary>Gets the module twin.</summary>
    public Task<Twin> GetTwinAsync(CancellationToken cancellationToken = default)
        => _core.GetTwinAsync(cancellationToken);

    /// <summary>Patches reported properties from contiguous JSON bytes. Returns the new twin version.</summary>
    public Task<long?> UpdateReportedPropertiesAsync(
        ReadOnlyMemory<byte> reportedJson, CancellationToken cancellationToken = default)
        => _core.UpdateReportedPropertiesAsync(new ReadOnlySequence<byte>(reportedJson), cancellationToken);

    /// <summary>
    /// Patches reported properties from JSON that may span multiple buffer segments (zero-copy).
    /// Returns the new twin version.
    /// </summary>
    public Task<long?> UpdateReportedPropertiesAsync(
        ReadOnlySequence<byte> reportedJson, CancellationToken cancellationToken = default)
        => _core.UpdateReportedPropertiesAsync(reportedJson, cancellationToken);

    /// <summary>Patches reported properties from a JSON string. Returns the new twin version.</summary>
    public Task<long?> UpdateReportedPropertiesAsync(string reportedJson, CancellationToken cancellationToken = default)
        => _core.UpdateReportedPropertiesAsync(
            new ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(reportedJson ?? "{}")), cancellationToken);

    /// <summary>
    /// Patches reported properties from a strongly-typed value serialized via a source-generated
    /// <paramref name="typeInfo"/> (AOT/trim safe). Returns the new twin version.
    /// </summary>
    public Task<long?> UpdateReportedPropertiesAsync<T>(
        T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
        => _core.UpdateReportedPropertiesAsync(
            new ReadOnlySequence<byte>(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo)), cancellationToken);

    /// <summary>Streams desired-property updates pushed by the service.</summary>
    public IAsyncEnumerable<DesiredPropertyUpdate> ReceiveDesiredPropertyUpdatesAsync(
        CancellationToken cancellationToken = default)
        => _core.ReceiveDesiredPropertyUpdatesAsync(cancellationToken);

    /// <summary>Disconnects and releases all resources.</summary>
    public ValueTask DisposeAsync() => _core.DisposeAsync();
}
