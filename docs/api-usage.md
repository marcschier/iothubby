# IoTHubby API usage guide

This guide covers the public IoTHubby SDK APIs for Azure IoT Hub devices, IoT Hub modules, IoT Edge modules, and Device Provisioning Service registration. IoTHubby speaks MQTT only; see the [MQTT wire mapping](mqtt-topics.md) for the exact topics, username/password shape, property bag encoding, and MQTT limitations.

## Installation

Install the packages from nuget.org:

```powershell
dotnet add package IoTHubby
dotnet add package IoTHubby.Edge
```

Use `IoTHubby` for device/module clients, telemetry, cloud-to-device messages, direct methods, twins, and DPS provisioning. Add `IoTHubby.Edge` when a module should bootstrap from the Azure IoT Edge runtime environment and Workload API.

## Creating a device client

Create a device client from an IoT Hub device connection string, connect once, and dispose it with `await using`:

```csharp
using IoTHubby;

var connectionString = Environment.GetEnvironmentVariable("IOTHUB_DEVICE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set IOTHUB_DEVICE_CONNECTION_STRING.");

await using var client = IoTHubDeviceClient.CreateFromConnectionString(connectionString);
await client.ConnectAsync();
```

For X.509 authentication, load the certificate in your own factory and pass it to `CreateWithClientCertificate`:

```csharp
using System.Security.Cryptography.X509Certificates;
using IoTHubby;

static X509Certificate2 LoadDeviceCertificate()
{
    var bytes = File.ReadAllBytes("device.pfx");
    return new X509Certificate2(bytes, "pfx-password", X509KeyStorageFlags.EphemeralKeySet);
}

await using var client = IoTHubDeviceClient.CreateWithClientCertificate(
    hostName: "contoso.azure-devices.net",
    deviceId: "device-01",
    certificate: LoadDeviceCertificate());

await client.ConnectAsync();
```

`CreateFromConnectionString` also accepts an optional `Action<IoTHubClientOptions>` for options such as product information, model ID, logging, reconnect behavior, SAS renewal, and TLS customization.

## Sending telemetry

Use `TelemetryMessage` as a mutable builder for payload bytes, MQTT QoS, system properties, and application properties. `TelemetryMessage.FromString` encodes the payload as UTF-8; the constructor accepts `ReadOnlyMemory<byte>` when you already have bytes.

```csharp
using IoTHubby;

var message = TelemetryMessage.FromString("{\"temperature\":21.5,\"humidity\":47}");
message.MessageId = Guid.NewGuid().ToString("N");
message.CorrelationId = "batch-42";
message.ContentType = "application/json";
message.ContentEncoding = "utf-8";
message.QoS = IoTHubQoS.AtLeastOnce;
message.Properties["sensor"] = "warehouse-7";
message.Properties["schema"] = "telemetry-v1";

await client.SendTelemetryAsync(message, cancellationToken);
```

IoT Hub over MQTT supports only `IoTHubQoS.AtMostOnce` and `IoTHubQoS.AtLeastOnce`; there is no QoS 2 and retain is not honored. Telemetry system and application properties are encoded into the MQTT topic property bag documented in [MQTT wire mapping](mqtt-topics.md).

## Receiving cloud-to-device messages

`ReceiveCloudToDeviceMessagesAsync` returns an `IAsyncEnumerable<CloudToDeviceMessage>` that streams until the enumeration is disposed or cancelled. Each delivered MQTT QoS 1 message is completed implicitly by the MQTT `PUBACK`; MQTT does not expose abandon or reject.

```csharp
using IoTHubby;

await foreach (var message in client.ReceiveCloudToDeviceMessagesAsync(cancellationToken))
{
    Console.WriteLine($"C2D payload: {message.PayloadAsString}");
    Console.WriteLine($"MessageId: {message.MessageId}");
    Console.WriteLine($"CorrelationId: {message.CorrelationId}");
    Console.WriteLine($"Content: {message.ContentType}; {message.ContentEncoding}");

    if (message.Properties.TryGetValue("command", out var command))
    {
        Console.WriteLine($"Application command: {command}");
    }
}
```

The payload is a retained managed copy, so `CloudToDeviceMessage` has nothing to dispose. `SystemProperties` contains raw system-property keys with their `$.` prefix; `Properties` contains application properties.

## Direct methods

Register a direct-method handler with `SetMethodHandlerAsync`. The handler receives a `DirectMethodRequest` with `Name`, raw `Payload`, and `PayloadAsString`, and returns a `DirectMethodResponse`.

```csharp
using IoTHubby;

await client.SetMethodHandlerAsync((request, ct) =>
{
    Console.WriteLine($"Method '{request.Name}' called with {request.PayloadAsString}");

    DirectMethodResponse response = request.Name switch
    {
        "reboot" => DirectMethodResponse.FromString(200, "{\"accepted\":true}"),
        "ping" => DirectMethodResponse.FromStatus(204),
        _ => DirectMethodResponse.FromBytes(404, ReadOnlyMemory<byte>.Empty),
    };

    return new ValueTask<DirectMethodResponse>(response);
}, cancellationToken);
```

Pass `null` to `SetMethodHandlerAsync` to stop handling methods; subsequent requests are answered with 404 by the client core.

## Device twin

Use `GetTwinAsync` to read the desired and reported property collections. Each collection exposes `Version`, raw JSON bytes through `RawJson`, `ToJsonString()`, `RootElement`, and an AOT-safe `Deserialize<T>(JsonTypeInfo<T>)`.

```csharp
using IoTHubby;

Twin twin = await client.GetTwinAsync(cancellationToken);

Console.WriteLine($"Desired v{twin.Desired.Version}: {twin.Desired.ToJsonString()}");
Console.WriteLine($"Reported v{twin.Reported.Version}: {twin.Reported.ToJsonString()}");
```

Patch reported properties from a JSON string, raw UTF-8 JSON bytes, or a strongly typed value serialized with a source-generated `JsonTypeInfo<T>`:

```csharp
using System.Text;
using IoTHubby;

long? stringVersion = await client.UpdateReportedPropertiesAsync(
    "{\"status\":\"online\"}",
    cancellationToken);

long? bytesVersion = await client.UpdateReportedPropertiesAsync(
    Encoding.UTF8.GetBytes("{\"uptimeSeconds\":123}"),
    cancellationToken);
```

For NativeAOT and trimming, prefer the source-generated overload for application models:

```csharp
using System.Text.Json.Serialization;
using IoTHubby;

long? version = await client.UpdateReportedPropertiesAsync(
    new ReportedState("online", 21.5),
    AppJsonContext.Default.ReportedState,
    cancellationToken);

public sealed record ReportedState(string Status, double Temperature);

[JsonSerializable(typeof(ReportedState))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
```

Desired-property patches are streamed as `DesiredPropertyUpdate` values:

```csharp
await foreach (var update in client.ReceiveDesiredPropertyUpdatesAsync(cancellationToken))
{
    Console.WriteLine($"Desired changed to v{update.Properties.Version}: {update.Properties.ToJsonString()}");
}
```

## Module client

`IoTHubModuleClient` mirrors the device APIs and adds IoT Edge module-to-module routing through named outputs and inputs.

```csharp
using IoTHubby;

var moduleConnectionString = Environment.GetEnvironmentVariable("IOTHUB_MODULE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Set IOTHUB_MODULE_CONNECTION_STRING.");

await using var module = IoTHubModuleClient.CreateFromConnectionString(moduleConnectionString);
await module.ConnectAsync(cancellationToken);

await module.SendTelemetryAsync(TelemetryMessage.FromString("{\"module\":\"telemetry\"}"), cancellationToken);
await module.SendToOutputAsync("output1", TelemetryMessage.FromString("{\"route\":\"output1\"}"), cancellationToken);

await foreach (var message in module.ReceiveInputMessagesAsync("input1", cancellationToken))
{
    Console.WriteLine($"Input {message.InputName}: {message.PayloadAsString}");
}
```

Pass an empty string to `ReceiveInputMessagesAsync` to receive messages from all inputs.

## IoT Edge bootstrap

When running inside the Azure IoT Edge runtime, `IoTHubby.Edge` can create an `IoTHubModuleClient` from the injected `IOTEDGE_*` environment variables. The Edge Workload API supplies SAS signing and the edge gateway trust bundle; the returned module client is not connected until you call `ConnectAsync`.

```csharp
using IoTHubby;
using IoTHubby.Edge;

await using IoTHubModuleClient module = await EdgeModuleClient.CreateFromEnvironmentAsync(
    options =>
    {
        options.ProductInfo = "contoso.edge-module/1.0";
        options.ModelId = "dtmi:contoso:EdgeModule;1";
    },
    cancellationToken);

await module.ConnectAsync(cancellationToken);
await module.SendTelemetryAsync(TelemetryMessage.FromString("{\"edge\":true}"), cancellationToken);
```

For local development outside the edge runtime, `EdgeModuleClient.CreateFromEnvironmentAsync` also accepts `EdgeHubConnectionString` or `IotHubConnectionString` from the environment and delegates to `IoTHubModuleClient.CreateFromConnectionString`.

## DPS provisioning

Use `IoTHubby.Provisioning.ProvisioningClient` for MQTT registration with Azure Device Provisioning Service. `RegisterAsync` connects, sends the register request, polls until a terminal result, and returns `DeviceRegistrationResult`.

```csharp
using IoTHubby.Provisioning;

await using var provisioning = ProvisioningClient.CreateWithSymmetricKey(
    idScope: "0ne00000000",
    registrationId: "device-01",
    symmetricKeyBase64: Environment.GetEnvironmentVariable("DPS_SYMMETRIC_KEY")!,
    options =>
    {
        options.Payload = "{\"modelId\":\"dtmi:contoso:Thermostat;1\"}";
        options.Timeout = TimeSpan.FromMinutes(2);
    });

DeviceRegistrationResult result = await provisioning.RegisterAsync(cancellationToken);

Console.WriteLine($"DPS status: {result.Status}");
Console.WriteLine($"Assigned hub: {result.AssignedHubOrThrow()}");
Console.WriteLine($"Device id: {result.DeviceId ?? result.RegistrationId}");
Console.WriteLine($"Operation id: {result.OperationId}");
```

X.509 individual enrollment uses the certificate overload:

```csharp
using System.Security.Cryptography.X509Certificates;
using IoTHubby.Provisioning;

X509Certificate2 certificate = LoadDeviceCertificate();

await using var provisioning = ProvisioningClient.CreateWithClientCertificate(
    idScope: "0ne00000000",
    registrationId: "device-01",
    certificate: certificate);

DeviceRegistrationResult result = await provisioning.RegisterAsync(cancellationToken);
```

`ProvisioningClientOptions` includes `GlobalEndpoint`, `Timeout`, `SasTokenLifetime`, `Payload`, `ConfigureTls`, and `LoggerFactory`.

## Options and DI seams

`IoTHubClientOptions` exposes the production knobs expected by device and module clients. Every option has a default, so configure only what your application needs.

```csharp
using Microsoft.Extensions.Logging;
using IoTHubby;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

await using var client = IoTHubDeviceClient.CreateFromConnectionString(
    connectionString,
    options =>
    {
        options.ProductInfo = "contoso.device/1.0";
        options.ModelId = "dtmi:contoso:Device;1";
        options.AutoReconnect = true;
        options.ReconnectInitialDelay = TimeSpan.FromSeconds(1);
        options.ReconnectMaxDelay = TimeSpan.FromSeconds(30);
        options.SasTokenLifetime = TimeSpan.FromHours(1);
        options.SasTokenRenewalFraction = 0.80;
        options.KeepAlive = TimeSpan.FromSeconds(60);
        options.OperationTimeout = TimeSpan.FromSeconds(30);
        options.ReceiveChannelCapacity = 2048;
        options.LoggerFactory = loggerFactory;
        options.ConfigureTls = tls =>
        {
            tls.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None;
        };
    });
```

`AutoReconnect` controls reconnect and re-subscription after dropped connections. `SasTokenRenewalFraction` controls when generated SAS tokens are proactively renewed by reconnecting with a fresh token. `ConfigureTls` is the seam for custom TLS settings such as an edge gateway trust bundle. `LoggerFactory` passes diagnostics to the MQTT transport and client internals.

## Connection lifecycle and events

Clients expose the current `State` and a `ConnectionStateChanged` event. `IoTHubConnectionState` values are `Disconnected`, `Connecting`, `Connected`, `Reconnecting`, and `Disposed`.

```csharp
client.ConnectionStateChanged += (_, args) =>
{
    Console.WriteLine($"IoT Hub state changed to {args.State}: {args.Reason}");
};

await client.ConnectAsync(cancellationToken);

if (client.State == IoTHubConnectionState.Connected)
{
    await client.SendTelemetryAsync(TelemetryMessage.FromString("{\"ready\":true}"), cancellationToken);
}

await client.DisconnectAsync(cancellationToken);
```

`DisconnectAsync` performs a graceful disconnect. `DisposeAsync` disconnects and releases subscriptions, channels, timers, and transport resources.

## Error handling

IoTHubby throws `IoTHubClientException` when IoT Hub or DPS rejects a connection or operation, when an operation returns a non-success status, or when a protocol response is invalid. `StatusCode` is populated when the failure came from an IoT Hub/DPS status response.

```csharp
try
{
    await client.ConnectAsync(cancellationToken);
    await client.SendTelemetryAsync(TelemetryMessage.FromString("{\"ok\":true}"), cancellationToken);
}
catch (IoTHubClientException ex) when (ex.StatusCode is int status)
{
    Console.Error.WriteLine($"IoT Hub returned status {status}: {ex.Message}");
}
catch (IoTHubClientException ex)
{
    Console.Error.WriteLine($"IoT Hub client error: {ex.Message}");
}
```

Use normal .NET cancellation patterns for user-initiated shutdown and timeouts; `OperationCanceledException` generally indicates the provided cancellation token was cancelled.

## AOT and trimming notes

The shipping libraries are marked NativeAOT-compatible and trimmable on `net8.0`, `net9.0`, and `net10.0`. JSON used internally by DPS and the Edge Workload API is source-generated, and the public twin APIs include `JsonTypeInfo<T>` overloads so application models can stay source-generated too.

Prefer `TwinProperties.Deserialize(AppJsonContext.Default.YourType)` and `UpdateReportedPropertiesAsync(value, AppJsonContext.Default.YourType)` over reflection-based `JsonSerializer` calls in NativeAOT or trimmed applications. Keep your own model contexts in application code, and pass raw JSON strings or UTF-8 bytes when no model binding is needed.

IoTHubby intentionally exposes MQTT-shaped behavior where MQTT differs from the service SDKs: C2D completion is implicit, abandon/reject are unavailable, QoS is limited to 0/1, retain is ignored, and file upload is out of scope because it requires HTTPS and Blob Storage.
