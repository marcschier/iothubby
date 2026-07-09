// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace IoTHubby;

/// <summary>
/// Pure (broker-independent) translation between IoTHubby message objects and the IoT Hub MQTT wire
/// format: building the telemetry topic + property bag, mapping inbound topics/payloads to
/// <see cref="CloudToDeviceMessage"/>, and parsing a twin document. Kept separate so the wire
/// behaviour is unit-testable without a live connection.
/// </summary>
internal static class IoTHubWireCodec
{
    /// <summary>Builds the telemetry (D2C or module-output) publish topic for a message.</summary>
    public static string BuildTelemetryTopic(IoTHubTopics topics, TelemetryMessage message, string? outputName)
    {
        var builder = new StringBuilder(topics.TelemetryPrefix.Length + 64);
        builder.Append(topics.TelemetryPrefix);
        PropertyBagCodec.Encode(builder, EnumerateTelemetryProperties(message, outputName));
        return builder.ToString();
    }

    private static IEnumerable<KeyValuePair<string, string?>> EnumerateTelemetryProperties(
        TelemetryMessage message, string? outputName)
    {
        var output = outputName ?? message.OutputName;
        if (!string.IsNullOrEmpty(output))
        {
            yield return new(SystemProperties.OutputName, output);
        }
        if (message.MessageId is not null)
        {
            yield return new(SystemProperties.MessageId, message.MessageId);
        }
        if (message.CorrelationId is not null)
        {
            yield return new(SystemProperties.CorrelationId, message.CorrelationId);
        }
        if (message.UserId is not null)
        {
            yield return new(SystemProperties.UserId, message.UserId);
        }
        if (message.ContentType is not null)
        {
            yield return new(SystemProperties.ContentType, message.ContentType);
        }
        if (message.ContentEncoding is not null)
        {
            yield return new(SystemProperties.ContentEncoding, message.ContentEncoding);
        }
        if (message.MessageSchema is not null)
        {
            yield return new(SystemProperties.MessageSchema, message.MessageSchema);
        }
        if (message.CreationTimeUtc is { } created)
        {
            yield return new(SystemProperties.CreationTimeUtc, created.UtcDateTime.ToString("O"));
        }
        if (message.ExpiryTimeUtc is { } expiry)
        {
            yield return new(SystemProperties.ExpiryTimeUtc, expiry.UtcDateTime.ToString("O"));
        }
        foreach (var kv in message.Properties)
        {
            yield return new(kv.Key, kv.Value);
        }
    }

    /// <summary>Maps an inbound message on a fixed-prefix topic (C2D) to a public message.</summary>
    public static CloudToDeviceMessage MapInbound(string topic, string prefix, ReadOnlyMemory<byte> payload)
    {
        var bag = TopicParser.PropertyBagAfter(topic, prefix);
        return MapMessage(payload, bag, inputName: null);
    }

    /// <summary>
    /// Maps a payload + already-extracted property bag (and optional input name) to a message.
    /// </summary>
    public static CloudToDeviceMessage MapMessage(
        ReadOnlyMemory<byte> payload,
        ReadOnlySpan<char> bag,
        string? inputName)
    {
        var decoded = PropertyBagCodec.Decode(bag);
        var system = new Dictionary<string, string>(StringComparer.Ordinal);
        var app = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in decoded)
        {
            (kv.Key.StartsWith("$.", StringComparison.Ordinal) ? system : app)[kv.Key] = kv.Value;
        }
        if (inputName is not null)
        {
            system[SystemProperties.InputName] = inputName;
        }
        return new CloudToDeviceMessage(payload.ToArray(), system, app);
    }

    /// <summary>
    /// Parses a full twin document (<c>{"desired":{...},"reported":{...}}</c>) into a
    /// <see cref="Twin"/>.
    /// </summary>
    public static Twin ParseTwin(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new Twin(ExtractCollection(root, "desired"), ExtractCollection(root, "reported"));
    }

    private static TwinProperties ExtractCollection(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var element))
        {
            long? version = element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("$version", out var v)
                && v.TryGetInt64(out var version64)
                ? version64
                : null;
            return new TwinProperties(Encoding.UTF8.GetBytes(element.GetRawText()), version);
        }
        return new TwinProperties(Encoding.UTF8.GetBytes("{}"), null);
    }
}
