# IoT Hub MQTT wire mapping

IoTHubby implements the documented Azure IoT Hub MQTT conventions. This page records the exact wire
mapping so behaviour is easy to verify against the reference `azure-iot-sdk-csharp` and the Azure docs.
IoT Hub speaks **MQTT 3.1.1** over TLS on port **8883**; only **QoS 0/1** are supported and the hub does
not honour retain.

## Connection

| Field | Value |
| --- | --- |
| Client id | `{deviceId}` (device) or `{deviceId}/{moduleId}` (module) |
| Username | `{iotHubHost}/{deviceId}[/{moduleId}]/?api-version=2021-04-12` (plus optional `&model-id=...` and `&DeviceClientType=...`) |
| Password | `SharedAccessSignature sr={audience}&sig={signature}&se={expiry}` (empty for X.509) |

The SAS `audience` is the URL-encoded resource URI `{host}/devices/{deviceId}[/modules/{moduleId}]`; the
`signature` is the URL-encoded Base64 HMAC-SHA256 of `{audience}\n{expiry}` keyed by the Base64-decoded
shared access key. SAS tokens are renewed proactively before expiry (a reconnect with a fresh token).

## Topics

| Purpose | Direction | Topic |
| --- | --- | --- |
| Telemetry (D2C) | publish | `devices/{deviceId}[/modules/{moduleId}]/messages/events/{propertyBag}` |
| Cloud-to-device | subscribe | `devices/{deviceId}/messages/devicebound/#` |
| Direct methods | subscribe | `$iothub/methods/POST/#` |
| Method request | inbound | `$iothub/methods/POST/{methodName}/?$rid={rid}` |
| Method response | publish | `$iothub/methods/res/{status}/?$rid={rid}` |
| Twin responses | subscribe | `$iothub/twin/res/#` |
| Twin GET | publish | `$iothub/twin/GET/?$rid={rid}` |
| Reported update | publish | `$iothub/twin/PATCH/properties/reported/?$rid={rid}` |
| Desired updates | subscribe | `$iothub/twin/PATCH/properties/desired/#` |
| Edge module input | subscribe | `devices/{deviceId}/modules/{moduleId}/inputs/#` |

Because IoT Hub is MQTT 3.1.1 (no MQTT 5 request/response), twin GET/PATCH correlate requests and
responses with a client-allocated `$rid` value over the shared `$iothub/twin/res/#` subscription.

## Property bag

Telemetry system and application properties are appended to the topic as a URL-encoded
`key=value&key=value` bag. System properties use a `$.` prefix, e.g. `$.mid` (message id), `$.cid`
(correlation id), `$.ct` (content type), `$.ce` (content encoding), `$.on` (edge output name). Keys and
values are percent-encoded per RFC 3986.

## Device Provisioning Service (DPS)

| Purpose | Topic |
| --- | --- |
| Username | `{idScope}/registrations/{registrationId}/api-version=2019-03-31` |
| Responses | `$dps/registrations/res/#` |
| Register | `$dps/registrations/PUT/iotdps-register/?$rid={rid}` |
| Poll status | `$dps/registrations/GET/iotdps-get-operationstatus/?$rid={rid}&operationId={op}` |

## Wire limitations

- Only QoS 0 and 1; no QoS 2. Retain is not honoured by the hub.
- Cloud-to-device messages are completed implicitly via the QoS 1 `PUBACK`; there is no abandon/reject
  over MQTT.
- File upload requires HTTPS + Blob storage and is out of scope for this MQTT-only SDK.
