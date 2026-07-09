// Copyright (c) marcschier. Licensed under the MIT License.

namespace IoTHubby;

/// <summary>
/// Well-known IoT Hub system-property keys used in the MQTT topic property bag. System properties are
/// distinguished from application properties by the <c>$.</c> prefix.
/// </summary>
internal static class SystemProperties
{
    public const string MessageId = "$.mid";
    public const string CorrelationId = "$.cid";
    public const string UserId = "$.uid";
    public const string To = "$.to";
    public const string ContentType = "$.ct";
    public const string ContentEncoding = "$.ce";
    public const string ExpiryTimeUtc = "$.exp";
    public const string CreationTimeUtc = "$.ctime";
    public const string MessageSchema = "$.schema";

    /// <summary>Output name for module-to-module (edge) routing on outbound messages.</summary>
    public const string OutputName = "$.on";

    /// <summary>Input name on inbound edge module messages.</summary>
    public const string InputName = "$.inp";

    /// <summary>Originating device id on messages relayed through the edge hub.</summary>
    public const string ConnectionDeviceId = "$.cdid";

    /// <summary>Originating module id on messages relayed through the edge hub.</summary>
    public const string ConnectionModuleId = "$.cmid";

    /// <summary>Component name for Plug and Play messages.</summary>
    public const string ComponentName = "$.sub";
}
