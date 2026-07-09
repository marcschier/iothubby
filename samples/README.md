# IoTHubby samples

Runnable quickstarts for the IoTHubby SDK. Connection material is read from environment variables, so
nothing is hard-coded and nothing connects unless you set the relevant variables.

```bash
export IOTHUB_DEVICE_CONNECTION_STRING="HostName=...;DeviceId=...;SharedAccessKey=..."

dotnet run --project samples/IoTHubby.Samples -- telemetry
dotnet run --project samples/IoTHubby.Samples -- c2d
dotnet run --project samples/IoTHubby.Samples -- methods
dotnet run --project samples/IoTHubby.Samples -- twin
```

For a module (set `IOTHUB_MODULE_CONNECTION_STRING`):

```bash
dotnet run --project samples/IoTHubby.Samples -- module
```

The `edge` scenario is meant to run inside the Azure IoT Edge runtime, where the identity and TLS trust
bundle are provided by the `IOTEDGE_*` environment variables and the Edge Workload API — no connection
string required:

```bash
dotnet run --project samples/IoTHubby.Samples -- edge
```

| Scenario | Demonstrates |
| --- | --- |
| `telemetry` | Device-to-cloud messages with system + application properties. |
| `c2d` | Streaming cloud-to-device messages with `IAsyncEnumerable`. |
| `methods` | Handling direct methods and returning a response. |
| `twin` | Get twin, update reported properties, stream desired-property updates. |
| `module` | Module-to-module output/input routing (edge). |
| `edge` | Bootstrapping a module from the IoT Edge environment + Workload API. |
