// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace IoTHubby;

/// <summary>
/// A device-to-cloud (or module-to-cloud) telemetry message.
/// </summary>
/// <remarks>
/// The payload is opaque to IoT Hub; system and application properties are carried in the MQTT topic
/// property bag. Application property keys must not begin with <c>$</c> (reserved for system
/// properties). Instances are mutable builders and are not thread-safe.
/// </remarks>
public sealed class TelemetryMessage
{
    /// <summary>Creates an empty message. Set <see cref="Payload"/> and properties as needed.</summary>
    public TelemetryMessage()
    {
    }

    /// <summary>Creates a message with the given contiguous payload bytes.</summary>
    public TelemetryMessage(ReadOnlyMemory<byte> payload) => Payload = new ReadOnlySequence<byte>(payload);

    /// <summary>Creates a message whose payload may span multiple buffer segments (zero-copy).</summary>
    public TelemetryMessage(ReadOnlySequence<byte> payload) => Payload = payload;

    /// <summary>Creates a message from a UTF-8 string payload.</summary>
    public static TelemetryMessage FromString(string text)
        => new(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty).AsMemory());

    /// <summary>
    /// Opaque payload bytes. May span multiple buffer segments; multi-segment payloads are sent
    /// without first being concatenated. Use <see cref="PayloadMemory"/> for a contiguous view.
    /// </summary>
    public ReadOnlySequence<byte> Payload { get; set; }

    /// <summary>
    /// Contiguous view of <see cref="Payload"/> (zero-copy when single-segment, which is the common
    /// case; otherwise copies). Also settable to assign the payload from a contiguous buffer.
    /// </summary>
    public ReadOnlyMemory<byte> PayloadMemory
    {
        get => Payload.IsSingleSegment ? Payload.First : Payload.ToArray();
        set => Payload = new ReadOnlySequence<byte>(value);
    }

    /// <summary>Delivery guarantee. Defaults to <see cref="IoTHubQoS.AtLeastOnce"/>.</summary>
    public IoTHubQoS QoS { get; set; } = IoTHubQoS.AtLeastOnce;

    /// <summary>Application-assigned message id (<c>$.mid</c>).</summary>
    public string? MessageId { get; set; }

    /// <summary>Correlation id for request/response patterns (<c>$.cid</c>).</summary>
    public string? CorrelationId { get; set; }

    /// <summary>User id (<c>$.uid</c>).</summary>
    public string? UserId { get; set; }

    /// <summary>Content type, e.g. <c>application/json</c> (<c>$.ct</c>).</summary>
    public string? ContentType { get; set; }

    /// <summary>Content encoding, e.g. <c>utf-8</c> (<c>$.ce</c>).</summary>
    public string? ContentEncoding { get; set; }

    /// <summary>Message schema (<c>$.schema</c>).</summary>
    public string? MessageSchema { get; set; }

    /// <summary>
    /// Output name for module-to-module routing (<c>$.on</c>). Set by
    /// <c>IoTHubModuleClient.SendToOutputAsync</c>; ignored for device telemetry.
    /// </summary>
    public string? OutputName { get; set; }

    /// <summary>Optional creation timestamp (<c>$.ctime</c>).</summary>
    public DateTimeOffset? CreationTimeUtc { get; set; }

    /// <summary>Optional absolute expiry (<c>$.exp</c>).</summary>
    public DateTimeOffset? ExpiryTimeUtc { get; set; }

    /// <summary>User-defined application properties (topic property bag). Never <c>null</c>.</summary>
    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
