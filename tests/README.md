# Test project map

The test tree covers unit tests, broker-backed integration tests, Edge behavior, and longer-running resilience harnesses. Some projects are being created in parallel; this map describes the intended layout even when a folder is not present in a local checkout yet.

| Project | Purpose |
| --- | --- |
| `IoTHubby.Tests` | Unit tests for the wire primitives: topic parsing/building, property-bag encoding, SAS token generation and renewal, request-id correlation, provisioning JSON, connection-string parsing, and other internal helpers. |
| `IoTHubby.IntegrationTests` | Broker-backed end-to-end tests using the `Mqtt.Client.Testing` in-process broker with a fake IoT Hub responder to verify telemetry, C2D, direct methods, twins, DPS, reconnect, and topic-level behavior without a live Azure dependency. |
| `IoTHubby.Edge.Tests` | IoT Edge and Workload API tests, including environment bootstrap, trust-bundle handling, workload SAS signing, and module input/output routing seams. |
| `IoTHubby.ChaosTests` | Soak and fault-injection console harness for long-running reconnect, renewal, broker fault, and protocol resilience scenarios. |
| `IoTHubby.FuzzTests` | SharpFuzz coverage-guided fuzzing of parsers and codecs such as MQTT topics, property bags, SAS parsing, and DPS/twin response handling. |

Run the normal test suite with:

```powershell
dotnet test IoTHubby.slnx -c Release
```

Chaos and fuzz tests are intentionally not part of the normal inner-loop test command; run them through their own workflows or scripts so they can control duration, corpus, broker faults, and crash artifact collection.

