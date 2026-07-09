// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace IoTHubby;

/// <summary>
/// A cloud-to-device (C2D) message, or an edge module input message, delivered to the client.
/// </summary>
/// <remarks>
/// The payload is a private, garbage-collected copy of the received bytes, so it may be retained and
/// read freely — there is nothing to dispose. Over MQTT, IoT Hub completes the message implicitly via
/// the QoS 1 acknowledgement; there is no abandon/reject.
/// </remarks>
public sealed class CloudToDeviceMessage
{
    internal CloudToDeviceMessage(
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> systemProperties,
        IReadOnlyDictionary<string, string> properties)
    {
        Payload = new ReadOnlySequence<byte>(payload);
        SystemProperties = systemProperties;
        Properties = properties;
    }

    /// <summary>Message payload bytes (a retained copy). May span multiple segments.</summary>
    public ReadOnlySequence<byte> Payload { get; }

    /// <summary>Contiguous view of <see cref="Payload"/> (zero-copy when single-segment).</summary>
    public ReadOnlyMemory<byte> PayloadMemory => Payload.IsSingleSegment ? Payload.First : Payload.ToArray();

    /// <summary>The payload decoded as a UTF-8 string.</summary>
    public string PayloadAsString => System.Text.Encoding.UTF8.GetString(PayloadMemory.ToArray());

    /// <summary>Raw system properties as they appeared in the topic (keys retain the <c>$.</c> prefix).</summary>
    public IReadOnlyDictionary<string, string> SystemProperties { get; }

    /// <summary>Application properties.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>Message id (<c>$.mid</c>), if present.</summary>
    public string? MessageId => Get(IoTHubby.SystemProperties.MessageId);

    /// <summary>Correlation id (<c>$.cid</c>), if present.</summary>
    public string? CorrelationId => Get(IoTHubby.SystemProperties.CorrelationId);

    /// <summary>User id (<c>$.uid</c>), if present.</summary>
    public string? UserId => Get(IoTHubby.SystemProperties.UserId);

    /// <summary>Content type (<c>$.ct</c>), if present.</summary>
    public string? ContentType => Get(IoTHubby.SystemProperties.ContentType);

    /// <summary>Content encoding (<c>$.ce</c>), if present.</summary>
    public string? ContentEncoding => Get(IoTHubby.SystemProperties.ContentEncoding);

    /// <summary>Input name (<c>$.inp</c>) for an edge module input message, if present.</summary>
    public string? InputName => Get(IoTHubby.SystemProperties.InputName);

    private string? Get(string key) => SystemProperties.TryGetValue(key, out var value) ? value : null;
}
