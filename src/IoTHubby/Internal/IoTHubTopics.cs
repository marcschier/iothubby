// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace IoTHubby;

/// <summary>
/// Builds the MQTT topic strings and CONNECT username/client-id for a specific device or module
/// identity, following the documented Azure IoT Hub conventions. Constant prefixes are precomputed so
/// the per-message telemetry path only appends the variable property bag.
/// </summary>
internal sealed class IoTHubTopics
{
    // $iothub/... method and twin topics are per-connection and identical for devices and modules.
    public const string MethodSubscribe = "$iothub/methods/POST/#";
    public const string TwinResponseSubscribe = "$iothub/twin/res/#";
    public const string TwinDesiredSubscribe = "$iothub/twin/PATCH/properties/desired/#";
    public const string TwinDesiredPrefix = "$iothub/twin/PATCH/properties/desired/";

    private const string MethodPostPrefix = "$iothub/methods/POST/";

    public IoTHubTopics(string deviceId, string? moduleId = null)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            throw new ArgumentException("Device id is required.", nameof(deviceId));
        }

        DeviceId = deviceId;
        ModuleId = moduleId;

        TelemetryPrefix = string.IsNullOrEmpty(moduleId)
            ? $"devices/{deviceId}/messages/events/"
            : $"devices/{deviceId}/modules/{moduleId}/messages/events/";

        CloudToDeviceSubscribe = $"devices/{deviceId}/messages/devicebound/#";
        CloudToDevicePrefix = $"devices/{deviceId}/messages/devicebound/";

        InputSubscribe = string.IsNullOrEmpty(moduleId)
            ? null
            : $"devices/{deviceId}/modules/{moduleId}/inputs/#";
        InputPrefix = string.IsNullOrEmpty(moduleId)
            ? null
            : $"devices/{deviceId}/modules/{moduleId}/inputs/";
    }

    public string DeviceId { get; }
    public string? ModuleId { get; }
    public bool IsModule => !string.IsNullOrEmpty(ModuleId);

    /// <summary>Constant prefix for telemetry (D2C) publishes; append the encoded property bag.</summary>
    public string TelemetryPrefix { get; }

    public string CloudToDeviceSubscribe { get; }
    public string CloudToDevicePrefix { get; }

    /// <summary>Edge module input subscription (<c>null</c> for device connections).</summary>
    public string? InputSubscribe { get; }

    /// <summary>Edge module input topic prefix (<c>null</c> for device connections).</summary>
    public string? InputPrefix { get; }

    /// <summary>The CONNECT client identifier: <c>{deviceId}</c> or <c>{deviceId}/{moduleId}</c>.</summary>
    public string ClientId => IsModule ? $"{DeviceId}/{ModuleId}" : DeviceId;

    /// <summary>
    /// The CONNECT username: <c>{host}/{deviceId}[/{moduleId}]/?api-version={ver}</c>. When
    /// <paramref name="productInfo"/> and/or <paramref name="modelId"/> are supplied they are
    /// appended (URL-encoded) as <c>DeviceClientType</c> and <c>model-id</c> respectively.
    /// </summary>
    public string BuildUsername(string host, string? productInfo = null, string? modelId = null)
    {
        var identity = IsModule ? $"{DeviceId}/{ModuleId}" : DeviceId;
        var username = $"{host}/{identity}/?api-version={IoTHubProtocol.ApiVersion}";
        if (!string.IsNullOrEmpty(modelId))
        {
            username += "&model-id=" + Uri.EscapeDataString(modelId!);
        }
        if (!string.IsNullOrEmpty(productInfo))
        {
            username += "&DeviceClientType=" + Uri.EscapeDataString(productInfo!);
        }
        return username;
    }

    /// <summary>Method request topic prefix used to extract the method name and <c>$rid</c>.</summary>
    public static string MethodRequestPrefix => MethodPostPrefix;

    /// <summary>
    /// Response topic for a direct-method invocation:
    /// <c>$iothub/methods/res/{status}/?$rid={rid}</c>.
    /// </summary>
    public static string MethodResponse(int status, string rid)
        => $"$iothub/methods/res/{status.ToString(CultureInfo.InvariantCulture)}/?$rid={rid}";

    /// <summary>Twin GET request topic: <c>$iothub/twin/GET/?$rid={rid}</c>.</summary>
    public static string TwinGet(string rid) => $"$iothub/twin/GET/?$rid={rid}";

    /// <summary>Reported-properties PATCH topic: <c>$iothub/twin/PATCH/properties/reported/?$rid={rid}</c>.</summary>
    public static string TwinReportedPatch(string rid)
        => $"$iothub/twin/PATCH/properties/reported/?$rid={rid}";
}
