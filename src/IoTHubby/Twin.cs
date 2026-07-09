// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IoTHubby;

/// <summary>
/// A read-only view over a twin JSON object (desired or reported properties), with an optional
/// <c>$version</c>.
/// </summary>
/// <remarks>
/// Access the data as a <see cref="System.Text.Json.JsonElement"/> via <see cref="RootElement"/>, or
/// deserialize it to your own type by passing a source-generated
/// <see cref="JsonTypeInfo{T}"/> — keeping the whole path AOT/trim safe.
/// </remarks>
public sealed class TwinProperties
{
    private readonly byte[] _json;
    private JsonDocument? _document;

    internal TwinProperties(byte[] json, long? version)
    {
        _json = json;
        Version = version;
    }

    /// <summary>The twin <c>$version</c>, when present.</summary>
    public long? Version { get; }

    /// <summary>The raw JSON bytes of this collection.</summary>
    public ReadOnlyMemory<byte> RawJson => _json;

    /// <summary>The JSON as a UTF-8 string.</summary>
    public string ToJsonString() => System.Text.Encoding.UTF8.GetString(_json);

    /// <summary>
    /// The root JSON element. The backing <see cref="JsonDocument"/> is parsed lazily and cached; it
    /// lives for the lifetime of this collection.
    /// </summary>
    public JsonElement RootElement => (_document ??= JsonDocument.Parse(_json)).RootElement;

    /// <summary>
    /// Deserializes the collection to <typeparamref name="T"/> using a source-generated
    /// <paramref name="typeInfo"/> (AOT/trim safe).
    /// </summary>
    public T? Deserialize<T>(JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize(_json, typeInfo);
}

/// <summary>
/// A device or module twin: its desired and reported property collections.
/// </summary>
public sealed class Twin
{
    internal Twin(TwinProperties desired, TwinProperties reported)
    {
        Desired = desired;
        Reported = reported;
    }

    /// <summary>Desired properties (service-owned; the device observes these).</summary>
    public TwinProperties Desired { get; }

    /// <summary>Reported properties (device-owned; the device writes these).</summary>
    public TwinProperties Reported { get; }
}

/// <summary>
/// A desired-property change pushed by the service. The <see cref="TwinProperties.Version"/> reflects
/// the new desired-properties version.
/// </summary>
public sealed class DesiredPropertyUpdate
{
    internal DesiredPropertyUpdate(TwinProperties properties) => Properties = properties;

    /// <summary>The changed desired properties (a JSON merge patch).</summary>
    public TwinProperties Properties { get; }
}
