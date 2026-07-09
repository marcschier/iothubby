# Source project map

The source tree contains the shipping IoTHubby packages. Both projects target `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, and `net10.0`; production assemblies are strong-named, marked AOT-compatible and trimmable on modern TFMs, and keep JSON on source-generated contexts where JSON is needed internally.

| Project | Contents |
| --- | --- |
| `IoTHubby` | Core device and module SDK: `IoTHubDeviceClient`, `IoTHubModuleClient`, telemetry, cloud-to-device messages, direct methods, twin APIs, connection state, options, exceptions, and DPS provisioning under `IoTHubby.Provisioning`. |
| `IoTHubby.Edge` | IoT Edge extensions: environment bootstrap through `EdgeModuleClient`, Edge Workload API integration for SAS signing and trust bundle retrieval, and module-to-module input/output routing support. |

## `IoTHubby`

The public surface is in the `IoTHubby` namespace. It includes device/module clients, `TelemetryMessage`, `CloudToDeviceMessage`, `DirectMethodRequest`/`DirectMethodResponse`, `Twin`/`TwinProperties`/`DesiredPropertyUpdate`, `IoTHubClientOptions`, `IoTHubClientException`, `IoTHubQoS`, and connection-state events.

`IoTHubby.Provisioning` contains the DPS MQTT registration client, provisioning options, source-generated provisioning JSON context, and `DeviceRegistrationResult`.

The `Internal/` folder holds the wire primitives: connection-string parsing, MQTT topic builders/parsers, property-bag encoding, SAS token generation and credentials renewal, request-id correlation, session orchestration, and the shared client core.

## `IoTHubby.Edge`

`IoTHubby.Edge` adds `EdgeModuleClient.CreateFromEnvironmentAsync`, which reads the IoT Edge runtime environment, uses the Workload API for SAS signing, applies the gateway trust bundle to TLS, and returns an `IoTHubModuleClient`.

`IoTHubby.Edge.Workload` contains the Workload API HTTP client and its source-generated JSON context. Edge-specific credential renewal is implemented by the workload SAS credentials provider.

