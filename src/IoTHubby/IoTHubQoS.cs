// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby;

/// <summary>
/// Delivery guarantee for a message. IoT Hub over MQTT supports only at-most-once and at-least-once
/// (there is no exactly-once / QoS 2, and broker-side retain is not honored).
/// </summary>
public enum IoTHubQoS
{
    /// <summary>Fire-and-forget (MQTT QoS 0). No acknowledgement.</summary>
    AtMostOnce = 0,

    /// <summary>Acknowledged delivery (MQTT QoS 1). The send completes once the hub acknowledges.</summary>
    AtLeastOnce = 1,
}
