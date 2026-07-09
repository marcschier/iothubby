// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby;

/// <summary>
/// Protocol constants shared across the SDK. Centralizes the Azure IoT Hub / DPS API version and the
/// well-known MQTT topic prefixes so wire behaviour is defined in exactly one place and is easy to bump.
/// </summary>
internal static class IoTHubProtocol
{
    /// <summary>
    /// IoT Hub service API version appended to the MQTT username. Mirrors the value used by the
    /// current <c>azure-iot-sdk-csharp</c> device client.
    /// </summary>
    public const string ApiVersion = "2021-04-12";

    /// <summary>
    /// Device Provisioning Service API version used in the DPS MQTT username and register/poll topics.
    /// </summary>
    public const string ProvisioningApiVersion = "2019-03-31";

    /// <summary>Default IoT Hub MQTT-over-TLS port.</summary>
    public const int SecureMqttPort = 8883;

    /// <summary>Global DPS endpoint host.</summary>
    public const string GlobalProvisioningHost = "global.azure-devices-provisioning.net";
}
