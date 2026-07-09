# IoTHubby

A high-performance, **NativeAOT-friendly and trimmable** .NET SDK for the **device and module side** of **Azure IoT Hub** and **Azure IoT Edge**, speaking **MQTT only** (built on the [`Mqtt.Client`](https://www.nuget.org/packages/Mqtt.Client) library). Sorry, no AMQP, no HTTP transport.

IoTHubby is a clean-room implementation with wire/byte-compatibility to the official Azure IoT Hub SDK (topic strings, username format, SAS token format, property-bag encoding, api-version) — the public API is designed for more modern and simpler ergonomics.

## Design goals 🎯

- **You don't manage plumbing.** Connect once; auto-reconnect, automatic re-subscription, SAS token auto-renewal, and rotation-triggered reconnect are handled for you.
- **Modern, ergonomic API.** `IAsyncEnumerable<T>` streams for cloud-to-device messages, direct-method requests, and desired-property updates; optional zero-copy inline handlers.
- **Opt-in DI seams.** Bring your own reconnect policy, SAS/token provider, `TimeProvider`, or logger — every seam has a sensible built-in default.
- **Maximum performance.** `Span<T>`, `BinaryPrimitives`, UTF-8 transcoding, `ArrayPool<T>`, and `IBufferWriter<byte>` on the hot paths (topic building, property-bag codec, SAS signing); pooled inbound payloads from `Mqtt.Client`.
- **AOT + trim clean** on net8.0/net9.0/net10.0; JSON handled through a source-generated `System.Text.Json` context.

## Packages 📦

| Package | Contents |
| --- | --- |
| `IoTHubby` | Device & module client, telemetry (D2C), C2D, direct methods, twin, DPS provisioning. |
| `IoTHubby.Edge` | IoT Edge module bootstrap from environment, Edge Workload API (SAS signing + trust bundle), module-to-module input/output routing. |

## Documentation 📚

| Guide | Contents |
| --- | --- |
| [API usage guide](docs/api-usage.md) | Installation, device and module clients, telemetry, C2D, direct methods, twins, Edge bootstrap, DPS, options, lifecycle, errors, and AOT notes. |
| [MQTT wire mapping](docs/mqtt-topics.md) | Exact Azure IoT Hub MQTT topics, username/password shapes, property bag encoding, DPS topics, and wire limits. |

## Samples

See [samples/README.md](samples/README.md) for runnable quickstarts covering telemetry, cloud-to-device messages, direct methods, twins, module routes, and IoT Edge environment bootstrap.

## Target frameworks

`netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`. On `netstandard2.0/2.1` some advanced, platform-specific features (e.g. the Edge Workload API over a Unix-domain socket / named pipe) degrade gracefully; `net8.0+` is fully featured.

## Wire compatibility & limitations

IoTHubby follows the documented Azure IoT Hub MQTT conventions. Note the MQTT wire limits of IoT Hub:

- Only **QoS 0 and 1** are supported (no QoS 2), and **retain** is not honored by the hub.
- Cloud-to-device messages are completed implicitly via the QoS 1 `PUBACK`; **abandon/reject are not available** over MQTT.
- **File upload** (which requires HTTPS + Blob storage) is **out of scope**.

## Status

Early development (0.9.x). API is approaching stable but may still change before 1.0.

## License

MIT — see [LICENSE](LICENSE).
